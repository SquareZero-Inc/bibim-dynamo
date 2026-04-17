// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;

namespace BIBIM_MVP
{
    /// <summary>
    /// Manages conversation context for error-resilient chat sessions.
    /// Preserves conversation state, workflow progress, and retry context
    /// to enable seamless retry without re-input after API failures.
    /// </summary>
    public class ConversationContextManager
    {
        private SessionContext _currentSession;
        private readonly LocalSessionManager _localSessionManager;

        /// <summary>
        /// Initializes a new instance of ConversationContextManager
        /// </summary>
        /// <param name="localSessionManager">Local session manager for persisting session context</param>
        public ConversationContextManager(LocalSessionManager localSessionManager)
        {
            _localSessionManager = localSessionManager ?? throw new ArgumentNullException(nameof(localSessionManager));
        }

        /// <summary>
        /// Starts a new conversation session with empty context.
        /// Requirements: 4.2
        /// </summary>
        /// <param name="sessionId">Unique identifier for the new session</param>
        public void StartNewSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));

            _currentSession = new SessionContext
            {
                SessionId = sessionId,
                Turns = new System.Collections.Generic.List<ConversationTurn>(),
                CurrentWorkflow = null,
                PendingRetry = null,
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 0
            };

            Logger.Log("ConversationContextManager", $"Started new session: {sessionId}");
        }

        /// <summary>
        /// Restores an existing session from persistent storage.
        /// Requirements: 4.3, 4.4, 5.2
        /// </summary>
        /// <param name="sessionId">Session ID to restore</param>
        /// <returns>The restored session context</returns>
        public SessionContext RestoreSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));

            try
            {
                Logger.Log("ConversationContextManager", $"Restoring session: {sessionId}");
                _currentSession = _localSessionManager.LoadSessionContext(sessionId);
                
                if (_currentSession == null)
                {
                    Logger.Log("ConversationContextManager", $"Session {sessionId} not found, creating new session");
                    StartNewSession(sessionId);
                }
                else
                {
                    Logger.Log("ConversationContextManager", $"Session restored: {sessionId}, Turns: {_currentSession.Turns.Count}");
                }

                return _currentSession;
            }
            catch (Exception ex)
            {
                Logger.Log("ConversationContextManager", $"Failed to restore session {sessionId}: {ex.Message}");
                // Create new session on failure
                StartNewSession(sessionId);
                return _currentSession;
            }
        }

        /// <summary>
        /// Adds a conversation turn to the current session.
        /// Requirements: 1.1, 1.2
        /// </summary>
        /// <param name="userMessage">User's message</param>
        /// <param name="assistantResponse">AI's response (null if error occurred)</param>
        /// <param name="isError">Whether this turn resulted in an error</param>
        public void AddTurn(string userMessage, string assistantResponse = null, bool isError = false)
        {
            if (_currentSession == null)
                throw new InvalidOperationException("No active session. Call StartNewSession or RestoreSession first.");

            if (string.IsNullOrEmpty(userMessage))
                throw new ArgumentException("User message cannot be null or empty", nameof(userMessage));

            var turn = new ConversationTurn
            {
                UserMessage = userMessage,
                AssistantResponse = assistantResponse,
                IsError = isError,
                Timestamp = DateTime.UtcNow
            };

            _currentSession.Turns.Add(turn);
            _currentSession.LastUpdated = DateTime.UtcNow;

            // Update consecutive error count
            if (isError)
            {
                _currentSession.ConsecutiveErrors++;
                Logger.Log("ConversationContextManager", $"Error turn added. Consecutive errors: {_currentSession.ConsecutiveErrors}");
            }
            else if (assistantResponse != null)
            {
                // Reset on successful response
                _currentSession.ConsecutiveErrors = 0;
                Logger.Log("ConversationContextManager", "Successful turn added. Consecutive errors reset to 0");
            }
        }

        /// <summary>
        /// Updates the workflow state for spec generation workflows.
        /// Requirements: 1.3, 1.4
        /// </summary>
        /// <param name="workflowState">Current workflow state</param>
        public void UpdateWorkflowState(WorkflowState workflowState)
        {
            if (_currentSession == null)
                throw new InvalidOperationException("No active session. Call StartNewSession or RestoreSession first.");

            _currentSession.CurrentWorkflow = workflowState;
            _currentSession.LastUpdated = DateTime.UtcNow;

            Logger.Log("ConversationContextManager", 
                $"Workflow state updated: Phase={workflowState?.Phase}, Document={workflowState?.DocumentPath}");
        }

        /// <summary>
        /// Creates a retry context for the failed request.
        /// Requirements: 1.4, 6.1
        /// </summary>
        /// <param name="userMessage">The original user message that failed</param>
        /// <param name="errorType">Type of error (RateLimit, Timeout, ServiceUnavailable, Unknown)</param>
        /// <returns>The created retry context</returns>
        public RetryContext CreateRetryContext(string userMessage, string errorType)
        {
            if (_currentSession == null)
                throw new InvalidOperationException("No active session. Call StartNewSession or RestoreSession first.");

            if (string.IsNullOrEmpty(userMessage))
                throw new ArgumentException("User message cannot be null or empty", nameof(userMessage));

            // Create a deep copy of conversation history
            var historyCopy = new System.Collections.Generic.List<ConversationTurn>();
            foreach (var turn in _currentSession.Turns)
            {
                historyCopy.Add(new ConversationTurn
                {
                    UserMessage = turn.UserMessage,
                    AssistantResponse = turn.AssistantResponse,
                    IsError = turn.IsError,
                    Timestamp = turn.Timestamp
                });
            }

            // Create a copy of workflow state if it exists
            WorkflowState workflowCopy = null;
            if (_currentSession.CurrentWorkflow != null)
            {
                workflowCopy = new WorkflowState
                {
                    Phase = _currentSession.CurrentWorkflow.Phase,
                    DocumentPath = _currentSession.CurrentWorkflow.DocumentPath,
                    PendingAction = _currentSession.CurrentWorkflow.PendingAction,
                    Metadata = new System.Collections.Generic.Dictionary<string, object>(
                        _currentSession.CurrentWorkflow.Metadata)
                };
            }

            var retryContext = new RetryContext
            {
                OriginalUserMessage = userMessage,
                ConversationHistory = historyCopy,
                WorkflowState = workflowCopy,
                FailedAt = DateTime.UtcNow,
                ErrorType = errorType
            };

            _currentSession.PendingRetry = retryContext;
            Logger.Log("ConversationContextManager", $"Retry context created: ErrorType={errorType}");

            return retryContext;
        }

        /// <summary>
        /// Gets the pending retry context if one exists.
        /// Requirements: 2.2, 6.1
        /// </summary>
        /// <returns>The pending retry context, or null if none exists</returns>
        public RetryContext GetPendingRetry()
        {
            if (_currentSession == null)
                return null;

            return _currentSession.PendingRetry;
        }

        /// <summary>
        /// Clears the pending retry context after successful retry.
        /// Requirements: 2.4, 6.4
        /// </summary>
        public void ClearPendingRetry()
        {
            if (_currentSession == null)
                throw new InvalidOperationException("No active session. Call StartNewSession or RestoreSession first.");

            _currentSession.PendingRetry = null;
            Logger.Log("ConversationContextManager", "Retry context cleared");
        }

        /// <summary>
        /// Saves the current session context to persistent storage.
        /// Requirements: 5.1, 5.2
        /// </summary>
        public void SaveSession()
        {
            if (_currentSession == null)
                throw new InvalidOperationException("No active session to save.");

            try
            {
                _localSessionManager.SaveSessionContext(_currentSession);
                Logger.Log("ConversationContextManager", $"Session saved: {_currentSession.SessionId}");
            }
            catch (Exception ex)
            {
                Logger.Log("ConversationContextManager", $"Failed to save session {_currentSession.SessionId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Checks if alternative guidance should be shown due to repeated errors.
        /// Requirements: 7.5
        /// </summary>
        /// <returns>True if 3 or more consecutive errors have occurred</returns>
        public bool ShouldShowAlternativeGuidance()
        {
            if (_currentSession == null)
                return false;

            return _currentSession.ConsecutiveErrors >= 3;
        }

        /// <summary>
        /// Gets the current session context.
        /// Requirements: 4.1, 4.5
        /// </summary>
        /// <returns>The current session context</returns>
        public SessionContext GetCurrentSession()
        {
            if (_currentSession == null)
                throw new InvalidOperationException("No active session. Call StartNewSession or RestoreSession first.");

            return _currentSession;
        }

        /// <summary>
        /// Gets the number of consecutive errors in the current session.
        /// Requirements: 7.4
        /// </summary>
        /// <returns>Number of consecutive errors</returns>
        public int GetConsecutiveErrorCount()
        {
            if (_currentSession == null)
                return 0;

            return _currentSession.ConsecutiveErrors;
        }
    }
}
