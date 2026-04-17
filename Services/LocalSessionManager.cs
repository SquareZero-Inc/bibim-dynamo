using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
#if NET48
using Newtonsoft.Json;
#else
using System.Text.Json;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Manages local storage of chat sessions in JSON format.
    /// Stores sessions at %APPDATA%/BIBIM/history/sessions.json
    ///</summary>
    public class LocalSessionManager
    {
        private readonly string _storageFolderPath;
        private readonly string _sessionsFilePath;
        private SessionStorage _cache;
        private readonly object _lock = new object();
        // Cloud sync removed for OSS BYOK build

        /// <summary>
        /// Creates a new LocalSessionManager with default storage path.
        /// </summary>
        public LocalSessionManager() : this(null) { }

        /// <summary>
        /// Creates a new LocalSessionManager with custom storage path (for testing).
        /// </summary>
        /// <param name="customStoragePath">Custom folder path, or null for default %APPDATA%/BIBIM/history</param>
        public LocalSessionManager(string customStoragePath)
        {
            if (string.IsNullOrEmpty(customStoragePath))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _storageFolderPath = Path.Combine(appData, "BIBIM", "history");
            }
            else
            {
                _storageFolderPath = customStoragePath;
            }
            _sessionsFilePath = Path.Combine(_storageFolderPath, "sessions.json");
            _cache = null;
        }

        /// <summary>
        /// <summary>
        /// Ensures the storage folder exists, creating it if necessary.
        /// </summary>
        public void EnsureStorageFolder()
        {
            if (!Directory.Exists(_storageFolderPath))
            {
                Directory.CreateDirectory(_storageFolderPath);
            }
        }

        /// <summary>
        /// Creates a new session with a unique ID and current timestamps.
        /// Requirements: 1.2
        /// </summary>
        /// <returns>A new ChatSession with unique SessionId</returns>
        public ChatSession CreateSession()
        {
            var now = DateTime.UtcNow;
            var (revitVersion, _, _, _) = GeminiService.GetCurrentConfigInfo();
            return new ChatSession
            {
                SessionId = Guid.NewGuid().ToString(),
                Title = "",
                RevitVersion = revitVersion ?? "",
                CreatedAt = now,
                UpdatedAt = now,
                Messages = new List<MessagePair>()
            };
        }

        /// <summary>
        /// Saves a session to local JSON storage.
        /// Skips saving if session has no messages.
        /// Updates existing session if ID matches, otherwise adds new.
        /// Also syncs to cloud if enabled.
        /// Requirements: 2.3, 6.3, 4.5, 4.6
        /// </summary>
        /// <param name="session">The session to save</param>
        public void SaveSession(ChatSession session)
        {
            if (session == null) return;
            
            // Skip saving if session has no messages (Requirement 6.3)
            if (session.Messages == null || session.Messages.Count == 0) return;

            lock (_lock)
            {
                EnsureStorageFolder();
                var storage = LoadStorage();

                // Find existing session by ID
                var existingIndex = storage.Sessions.FindIndex(s => s.SessionId == session.SessionId);
                
                if (existingIndex >= 0)
                {
                    // Update existing session
                    storage.Sessions[existingIndex] = session;
                }
                else
                {
                    // Add new session
                    storage.Sessions.Add(session);
                }

                WriteStorage(storage);
                _cache = storage;
            }

        }

        private void Log(string message) => Logger.Log("LocalSessionManager", message);

        /// <summary>
        /// Loads a session by its ID.
        /// Requirements: 3.2
        /// </summary>
        /// <param name="sessionId">The session ID to load</param>
        /// <returns>The session if found, null otherwise</returns>
        public ChatSession LoadSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return null;

            lock (_lock)
            {
                var storage = LoadStorage();
                return storage.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            }
        }

        /// <summary>
        /// Gets all sessions sorted by UpdatedAt descending (newest first).
        /// Requirements: 3.1
        /// </summary>
        /// <returns>List of sessions sorted by UpdatedAt descending</returns>
        public List<ChatSession> GetAllSessions()
        {
            lock (_lock)
            {
                var storage = LoadStorage();
                return storage.Sessions
                    .OrderByDescending(s => s.UpdatedAt)
                    .ToList();
            }
        }

        /// <summary>
        /// Adds a message pair to a session and auto-saves.
        /// DEPRECATED: Use AddSingleMessage for new code.
        /// Requirements: 2.2, 2.5
        /// </summary>
        /// <param name="sessionId">The session ID to add the message to</param>
        /// <param name="pair">The message pair to add</param>
        public void AddMessagePair(string sessionId, MessagePair pair)
        {
            if (string.IsNullOrEmpty(sessionId) || pair == null) return;

            lock (_lock)
            {
                var storage = LoadStorage();
                var session = storage.Sessions.FirstOrDefault(s => s.SessionId == sessionId);

                if (session == null)
                {
                    // Session not found - create a new one with this ID
                    var (revitVersion, _, _, _) = GeminiService.GetCurrentConfigInfo();
                    session = new ChatSession
                    {
                        SessionId = sessionId,
                        Title = GenerateTitle(pair.UserPrompt),
                        RevitVersion = revitVersion ?? "",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Messages = new List<MessagePair>()
                    };
                    storage.Sessions.Add(session);
                }

                // Set sequence order based on current message count
                pair.SequenceOrder = session.Messages.Count + 1;
                
                // Append message to session
                session.Messages.Add(pair);
                
                // Update session's UpdatedAt timestamp (Requirement 2.5)
                session.UpdatedAt = DateTime.UtcNow;

                // Auto-save after adding
                WriteStorage(storage);
                _cache = storage;
            }
        }

        /// <summary>
        /// Adds a single message to a session and auto-saves.
        /// Use this for accurate history recording with proper ordering.
        /// </summary>
        /// <param name="sessionId">The session ID to add the message to</param>
        /// <param name="role">Message sender: "user" or "assistant"</param>
        /// <param name="contentType">Content type: "text", "question", "spec", "code", "guide", "analysis"</param>
        /// <param name="content">Message content</param>
        /// <param name="pythonCode">Python code if contentType is "code"</param>
        public void AddSingleMessage(string sessionId, string role, string contentType, string content, string pythonCode = null)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(content)) return;

            lock (_lock)
            {
                var storage = LoadStorage();
                var session = storage.Sessions.FirstOrDefault(s => s.SessionId == sessionId);

                if (session == null)
                {
                    // Session not found - create a new one with this ID
                    var (revitVersion, _, _, _) = GeminiService.GetCurrentConfigInfo();
                    session = new ChatSession
                    {
                        SessionId = sessionId,
                        Title = role == "user" ? GenerateTitle(content) : "",
                        RevitVersion = revitVersion ?? "",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Messages = new List<MessagePair>(),
                        SingleMessages = new List<SingleMessage>()
                    };
                    storage.Sessions.Add(session);
                }

                // Ensure SingleMessages list exists
                if (session.SingleMessages == null)
                {
                    session.SingleMessages = new List<SingleMessage>();
                }

                // Create and add the message
                var message = new SingleMessage
                {
                    Role = role,
                    ContentType = contentType,
                    Content = content,
                    PythonCode = pythonCode,
                    SequenceOrder = session.SingleMessages.Count + 1,
                    CreatedAt = DateTime.UtcNow
                };

                session.SingleMessages.Add(message);

                // Auto-generate title from first user message if empty
                if (string.IsNullOrEmpty(session.Title) && role == "user")
                {
                    session.Title = GenerateTitle(content);
                }

                // Update session's UpdatedAt timestamp
                session.UpdatedAt = DateTime.UtcNow;

                // Auto-save after adding
                WriteStorage(storage);
                _cache = storage;
            }
        }

        /// <summary>
        /// Generates a session title from the first user prompt.
        /// Truncates to 50 characters with "..." if needed.
        /// Removes newlines and carriage returns.
        /// Requirements: 5.1, 5.2, 5.3
        /// </summary>
        /// <param name="userPrompt">The user prompt to generate title from</param>
        /// <returns>Generated title</returns>
        public string GenerateTitle(string userPrompt)
        {
            if (string.IsNullOrEmpty(userPrompt)) return "";

            // Remove newlines and carriage returns (Requirement 5.3)
            var cleaned = userPrompt
                .Replace("\r\n", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            // Truncate to 50 characters with "..." if needed (Requirements 5.1, 5.2)
            if (cleaned.Length > 50)
            {
                return cleaned.Substring(0, 47) + "...";
            }

            return cleaned;
        }

        /// <summary>
        /// Deletes a session by ID.
        /// </summary>
        /// <param name="sessionId">The session ID to delete</param>
        public void DeleteSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            lock (_lock)
            {
                var storage = LoadStorage();
                storage.Sessions.RemoveAll(s => s.SessionId == sessionId);
                WriteStorage(storage);
                _cache = storage;
            }
        }

        /// <summary>
        /// Gets the count of sessions in storage (for testing).
        /// </summary>
        /// <returns>Number of sessions</returns>
        public int GetSessionCount()
        {
            lock (_lock)
            {
                var storage = LoadStorage();
                return storage.Sessions.Count;
            }
        }

        /// <summary>
        /// Clears all sessions (for testing).
        /// </summary>
        public void ClearAllSessions()
        {
            lock (_lock)
            {
                var storage = new SessionStorage();
                WriteStorage(storage);
                _cache = storage;
            }
        }

        #region Migration (Disabled)

        /// <summary>
        /// Migration from conversations.json is disabled.
        /// sessions.json is shared across all Revit versions (2024/2026).
        /// </summary>
        public int MigrateFromOldFormat()
        {
            // Migration disabled - sessions.json is already cross-version compatible
            return 0;
        }

        /// <summary>
        /// Migration is no longer needed.
        /// </summary>
        public bool NeedsMigration()
        {
            return false;
        }

        #endregion

        #region Error-Resilient Context Management

        /// <summary>
        /// Saves a SessionContext to the specified session.
        /// Requirements: 5.1, 5.2
        /// </summary>
        /// <param name="context">The session context to save</param>
        public void SaveSessionContext(SessionContext context)
        {
            if (context == null) return;

            try
            {
                lock (_lock)
                {
                    EnsureStorageFolder();
                    var storage = LoadStorage();

                    // Find existing session by ID
                    var session = storage.Sessions.FirstOrDefault(s => s.SessionId == context.SessionId);

                    if (session != null)
                    {
                        // Update existing session's context data
                        session.ContextData = JsonHelper.Serialize(context);
                        session.UpdatedAt = context.LastUpdated;
                    }
                    else
                    {
                        // Create new session with context data
                        var (revitVersion, _, _, _) = GeminiService.GetCurrentConfigInfo();
                        session = new ChatSession
                        {
                            SessionId = context.SessionId,
                            Title = "",
                            RevitVersion = revitVersion ?? "",
                            CreatedAt = context.LastUpdated,
                            UpdatedAt = context.LastUpdated,
                            Messages = new List<MessagePair>(),
                            SingleMessages = new List<SingleMessage>(),
                            ContextData = JsonHelper.Serialize(context)
                        };
                        storage.Sessions.Add(session);
                    }

                    WriteStorage(storage);
                    _cache = storage;
                }

                Logger.Log("LocalSessionManager", $"Session context saved: {context.SessionId}");
            }
            catch (Exception ex)
            {
                Logger.Log("LocalSessionManager", $"Failed to save session context: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a SessionContext from the specified session.
        /// Requirements: 5.1, 4.3
        /// </summary>
        /// <param name="sessionId">The session ID to load context from</param>
        /// <returns>The session context, or a new empty context if not found</returns>
        public SessionContext LoadSessionContext(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return CreateEmptySessionContext(sessionId);
            }

            try
            {
                lock (_lock)
                {
                    var storage = LoadStorage();
                    var session = storage.Sessions.FirstOrDefault(s => s.SessionId == sessionId);

                    if (session != null && !string.IsNullOrEmpty(session.ContextData))
                    {
                        var context = JsonHelper.Deserialize<SessionContext>(session.ContextData);
                        Logger.Log("LocalSessionManager", $"Session context loaded: {sessionId}");
                        return context;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("LocalSessionManager", $"Failed to load session context: {ex.Message}");
            }

            // Return empty context if not found or error occurred
            return CreateEmptySessionContext(sessionId);
        }

        /// <summary>
        /// Creates an empty SessionContext for a new session.
        /// Requirements: 4.2
        /// </summary>
        /// <param name="sessionId">The session ID for the new context</param>
        /// <returns>A new empty SessionContext</returns>
        public SessionContext CreateEmptySessionContext(string sessionId)
        {
            return new SessionContext
            {
                SessionId = sessionId,
                Turns = new List<ConversationTurn>(),
                CurrentWorkflow = null,
                PendingRetry = null,
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 0
            };
        }

        #endregion

        #region Private Methods

        private SessionStorage LoadStorage()
        {
            if (_cache != null) return _cache;

            if (!File.Exists(_sessionsFilePath))
            {
                _cache = new SessionStorage();
                return _cache;
            }

            try
            {
                var json = File.ReadAllText(_sessionsFilePath);
                _cache = JsonHelper.Deserialize<SessionStorage>(json) ?? new SessionStorage();
            }
            catch (Exception ex)
            {
                // JSON parse error — backup corrupt file, then start fresh
                Logger.Log("LocalSessionManager.LoadStorage", $"Failed to parse sessions.json, starting fresh: {ex.Message}");
                try
                {
                    string corruptPath = _sessionsFilePath + ".corrupt";
                    File.Copy(_sessionsFilePath, corruptPath, overwrite: true);
                    Logger.Log("LocalSessionManager.LoadStorage", $"Corrupt file backed up to: {corruptPath}");
                }
                catch { }
                _cache = new SessionStorage();
            }

            return _cache;
        }

        private void WriteStorage(SessionStorage storage)
        {
            try
            {
                EnsureStorageFolder();
                var json = JsonHelper.Serialize(storage, indented: true);
                File.WriteAllText(_sessionsFilePath, json);
            }
            catch (Exception ex)
            {
                // Write permission error - continue with in-memory only
                Logger.Log("LocalSessionManager.WriteStorage", $"Failed to write sessions.json: {ex.Message}");
            }
        }

        #endregion
    }
}
