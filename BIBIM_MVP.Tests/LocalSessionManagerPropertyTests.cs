// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using BIBIM_MVP;

namespace BIBIM_MVP.Tests
{
    /// <summary>
    /// Property-based tests for LocalSessionManager.
    /// Feature: chat-session-management
    /// </summary>
    public class LocalSessionManagerPropertyTests : IDisposable
    {
        private readonly string _testStoragePath;
        private readonly LocalSessionManager _manager;

        public LocalSessionManagerPropertyTests()
        {
            // Create unique temp folder for each test run
            _testStoragePath = Path.Combine(Path.GetTempPath(), "BIBIM_Tests", Guid.NewGuid().ToString());
            _manager = new LocalSessionManager(_testStoragePath);
        }

        public void Dispose()
        {
            // Cleanup test folder
            try
            {
                if (Directory.Exists(_testStoragePath))
                {
                    Directory.Delete(_testStoragePath, true);
                }
            }
            catch { }
        }

        /// <summary>
        /// Property 1: Unique Session IDs
        /// For any number of sessions created by the Session_Manager, all session IDs should be unique (no duplicates).
        /// Validates: Requirements 1.2
        /// </summary>
        [Property(MaxTest = 100)]
        public Property UniqueSessionIds()
        {
            return Prop.ForAll(
                Gen.Choose(2, 20).ToArbitrary(),
                count =>
                {
                    var sessions = new List<ChatSession>();
                    for (int i = 0; i < count; i++)
                    {
                        sessions.Add(_manager.CreateSession());
                    }

                    var ids = sessions.Select(s => s.SessionId).ToList();
                    var uniqueIds = ids.Distinct().ToList();

                    return ids.Count == uniqueIds.Count;
                });
        }

        /// <summary>
        /// Property 2: Message Pair Append Integrity
        /// For any session with N messages, when a new MessagePair is added, the session should have exactly N+1 messages,
        /// and the last message should equal the added MessagePair.
        /// Validates: Requirements 2.2, 3.4
        /// </summary>
        [Property(MaxTest = 100)]
        public Property MessagePairAppendIntegrity()
        {
            return Prop.ForAll(
                Arb.From<NonEmptyString>(),
                Arb.From<NonEmptyString>(),
                Gen.Choose(0, 10).ToArbitrary(),
                (NonEmptyString prompt, NonEmptyString response, int initialCount) =>
                {
                    // Create a fresh manager for this test
                    var testPath = Path.Combine(Path.GetTempPath(), "BIBIM_Tests", Guid.NewGuid().ToString());
                    var manager = new LocalSessionManager(testPath);
                    
                    try
                    {
                        // Create session and add initial messages
                        var session = manager.CreateSession();
                        session.Title = "Test Session";
                        
                        for (int i = 0; i < initialCount; i++)
                        {
                            var pair = new MessagePair
                            {
                                UserPrompt = $"Prompt {i}",
                                AiResponse = $"Response {i}",
                                PythonCode = "",
                                CreatedAt = DateTime.UtcNow
                            };
                            session.Messages.Add(pair);
                        }
                        
                        // Save initial session if it has messages
                        if (session.Messages.Count > 0)
                        {
                            manager.SaveSession(session);
                        }

                        // Add new message pair
                        var newPair = new MessagePair
                        {
                            UserPrompt = prompt.Get,
                            AiResponse = response.Get,
                            PythonCode = "print('test')",
                            CreatedAt = DateTime.UtcNow
                        };
                        
                        manager.AddMessagePair(session.SessionId, newPair);

                        // Load and verify
                        var loaded = manager.LoadSession(session.SessionId);
                        
                        var hasCorrectCount = loaded.Messages.Count == initialCount + 1;
                        var lastMessage = loaded.Messages.Last();
                        var lastMessageMatches = lastMessage.UserPrompt == prompt.Get 
                            && lastMessage.AiResponse == response.Get;

                        return hasCorrectCount && lastMessageMatches;
                    }
                    finally
                    {
                        try { Directory.Delete(testPath, true); } catch { }
                    }
                });
        }

        /// <summary>
        /// Property 4: UpdatedAt Monotonicity
        /// For any session modification (adding message), the updatedAt timestamp should be greater than or equal to the previous updatedAt value.
        /// Validates: Requirements 2.5
        /// </summary>
        [Property(MaxTest = 100)]
        public Property UpdatedAtMonotonicity()
        {
            return Prop.ForAll(
                Arb.From<NonEmptyString>(),
                Gen.Choose(1, 5).ToArbitrary(),
                (NonEmptyString prompt, int messageCount) =>
                {
                    var testPath = Path.Combine(Path.GetTempPath(), "BIBIM_Tests", Guid.NewGuid().ToString());
                    var manager = new LocalSessionManager(testPath);
                    
                    try
                    {
                        var session = manager.CreateSession();
                        var previousUpdatedAt = session.UpdatedAt;
                        var allMonotonic = true;

                        for (int i = 0; i < messageCount; i++)
                        {
                            System.Threading.Thread.Sleep(1); // Ensure time passes
                            
                            var pair = new MessagePair
                            {
                                UserPrompt = $"{prompt.Get} {i}",
                                AiResponse = "Response",
                                PythonCode = "",
                                CreatedAt = DateTime.UtcNow
                            };
                            
                            manager.AddMessagePair(session.SessionId, pair);
                            
                            var loaded = manager.LoadSession(session.SessionId);
                            if (loaded.UpdatedAt < previousUpdatedAt)
                            {
                                allMonotonic = false;
                                break;
                            }
                            previousUpdatedAt = loaded.UpdatedAt;
                        }

                        return allMonotonic;
                    }
                    finally
                    {
                        try { Directory.Delete(testPath, true); } catch { }
                    }
                });
        }

        /// <summary>
        /// Property 5: Session List Ordering
        /// For any list of sessions returned by GetAllSessions(), the sessions should be sorted by updatedAt in descending order (newest first).
        /// Validates: Requirements 3.1
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SessionListOrdering()
        {
            return Prop.ForAll(
                Gen.Choose(2, 10).ToArbitrary(),
                count =>
                {
                    var testPath = Path.Combine(Path.GetTempPath(), "BIBIM_Tests", Guid.NewGuid().ToString());
                    var manager = new LocalSessionManager(testPath);
                    
                    try
                    {
                        // Create multiple sessions with messages
                        for (int i = 0; i < count; i++)
                        {
                            var session = manager.CreateSession();
                            session.Title = $"Session {i}";
                            session.Messages.Add(new MessagePair
                            {
                                UserPrompt = $"Prompt {i}",
                                AiResponse = $"Response {i}",
                                PythonCode = "",
                                CreatedAt = DateTime.UtcNow
                            });
                            // Vary the UpdatedAt to ensure different timestamps
                            session.UpdatedAt = DateTime.UtcNow.AddMinutes(-count + i);
                            manager.SaveSession(session);
                        }

                        var sessions = manager.GetAllSessions();
                        
                        // Verify descending order by UpdatedAt
                        for (int i = 0; i < sessions.Count - 1; i++)
                        {
                            if (sessions[i].UpdatedAt < sessions[i + 1].UpdatedAt)
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                    finally
                    {
                        try { Directory.Delete(testPath, true); } catch { }
                    }
                });
        }

        /// <summary>
        /// Property 6: Session Load Round-Trip
        /// For any session with messages, saving then loading by sessionId should return an equivalent session with all messages in correct sequence order.
        /// Validates: Requirements 3.2
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SessionLoadRoundTrip()
        {
            return Prop.ForAll(
                Arb.From<NonEmptyString>(),
                Arb.From<NonEmptyString>(),
                Gen.Choose(1, 5).ToArbitrary(),
                (NonEmptyString title, NonEmptyString prompt, int messageCount) =>
                {
                    var testPath = Path.Combine(Path.GetTempPath(), "BIBIM_Tests", Guid.NewGuid().ToString());
                    var manager = new LocalSessionManager(testPath);
                    
                    try
                    {
                        // Create and populate session
                        var session = manager.CreateSession();
                        session.Title = title.Get;
                        session.RevitVersion = "2025";
                        
                        for (int i = 0; i < messageCount; i++)
                        {
                            session.Messages.Add(new MessagePair
                            {
                                UserPrompt = $"{prompt.Get} {i}",
                                AiResponse = $"Response {i}",
                                PythonCode = $"print({i})",
                                SequenceOrder = i + 1,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                        
                        // Save
                        manager.SaveSession(session);
                        
                        // Load
                        var loaded = manager.LoadSession(session.SessionId);
                        
                        // Verify equivalence
                        var idMatches = loaded.SessionId == session.SessionId;
                        var titleMatches = loaded.Title == session.Title;
                        var messageCountMatches = loaded.Messages.Count == session.Messages.Count;
                        
                        var messagesMatch = true;
                        for (int i = 0; i < session.Messages.Count; i++)
                        {
                            if (loaded.Messages[i].UserPrompt != session.Messages[i].UserPrompt ||
                                loaded.Messages[i].AiResponse != session.Messages[i].AiResponse ||
                                loaded.Messages[i].SequenceOrder != session.Messages[i].SequenceOrder)
                            {
                                messagesMatch = false;
                                break;
                            }
                        }

                        return idMatches && titleMatches && messageCountMatches && messagesMatch;
                    }
                    finally
                    {
                        try { Directory.Delete(testPath, true); } catch { }
                    }
                });
        }

        /// <summary>
        /// Property 7: Title Generation Rules
        /// For any user prompt, the generated session title should:
        /// - Be at most 50 characters
        /// - Not contain newline (
) or carriage return () characters
        /// - End with "..." if the original prompt was longer than 50 characters
        /// Validates: Requirements 5.1, 5.2, 5.3
        /// </summary>
        [Property(MaxTest = 100)]
        public Property TitleGenerationRules()
        {
            return Prop.ForAll(
                Arb.From<NonEmptyString>(),
                (NonEmptyString prompt) =>
                {
                    var title = _manager.GenerateTitle(prompt.Get);
                    
                    // Rule 1: At most 50 characters
                    var lengthOk = title.Length <= 50;
                    
                    // Rule 2: No newlines or carriage returns
                    var noNewlines = !title.Contains("
") && !title.Contains("");
                    
                    // Rule 3: Ends with "..." if original was longer than 50 chars
                    var cleanedPrompt = prompt.Get.Replace("
", " ").Replace("", " ").Replace("
", " ").Trim();
                    var ellipsisOk = cleanedPrompt.Length <= 50 || title.EndsWith("...");

                    return lengthOk && noNewlines && ellipsisOk;
                });
        }

        /// <summary>
        /// Property 8: Empty Session Not Saved
        /// For any session with zero messages, calling SaveSession should not create a new entry in storage (storage count should remain unchanged).
        /// Validates: Requirements 6.3
        /// </summary>
        [Property(MaxTest = 100)]
        public Property EmptySessionNotSaved()
        {
            return Prop.ForAll(
                Gen.Choose(0, 5).ToArbitrary(),
                initialSessionCount =>
                {
                    var testPath = Path.Combine(Path.GetTempPath(), "BIBIM_Tests", Guid.NewGuid().ToString());
                    var manager = new LocalSessionManager(testPath);
                    
                    try
                    {
                        // Create initial sessions with messages
                        for (int i = 0; i < initialSessionCount; i++)
                        {
                            var session = manager.CreateSession();
                            session.Title = $"Session {i}";
                            session.Messages.Add(new MessagePair
                            {
                                UserPrompt = $"Prompt {i}",
                                AiResponse = $"Response {i}",
                                PythonCode = "",
                                CreatedAt = DateTime.UtcNow
                            });
                            manager.SaveSession(session);
                        }

                        var countBefore = manager.GetSessionCount();

                        // Try to save an empty session
                        var emptySession = manager.CreateSession();
                        emptySession.Title = "Empty Session";
                        // No messages added
                        manager.SaveSession(emptySession);

                        var countAfter = manager.GetSessionCount();

                        return countBefore == countAfter;
                    }
                    finally
                    {
                        try { Directory.Delete(testPath, true); } catch { }
                    }
                });
        }

        #region Error-Resilient Context Management Tests

        /// <summary>
        /// Unit Test: SaveSessionContext and LoadSessionContext basic functionality
        /// Validates: Requirements 5.1, 4.3
        /// </summary>
        [Fact]
        public void SaveAndLoadSessionContext_BasicFunctionality()
        {
            // Arrange
            var sessionId = Guid.NewGuid().ToString();
            var context = new SessionContext
            {
                SessionId = sessionId,
                Turns = new List<ConversationTurn>
                {
                    new ConversationTurn
                    {
                        UserMessage = "Hello",
                        AssistantResponse = "Hi there!",
                        IsError = false,
                        Timestamp = DateTime.UtcNow
                    }
                },
                CurrentWorkflow = new WorkflowState
                {
                    Phase = "requirements",
                    DocumentPath = ".kiro/specs/test/requirements.md",
                    PendingAction = "confirm",
                    Metadata = new Dictionary<string, object> { { "key", "value" } }
                },
                PendingRetry = null,
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 0
            };

            // Act
            _manager.SaveSessionContext(context);
            var loaded = _manager.LoadSessionContext(sessionId);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(sessionId, loaded.SessionId);
            Assert.Single(loaded.Turns);
            Assert.Equal("Hello", loaded.Turns[0].UserMessage);
            Assert.Equal("Hi there!", loaded.Turns[0].AssistantResponse);
            Assert.NotNull(loaded.CurrentWorkflow);
            Assert.Equal("requirements", loaded.CurrentWorkflow.Phase);
        }

        /// <summary>
        /// Unit Test: LoadSessionContext returns empty context for non-existent session
        /// Validates: Requirements 4.2
        /// </summary>
        [Fact]
        public void LoadSessionContext_NonExistentSession_ReturnsEmptyContext()
        {
            // Arrange
            var nonExistentId = Guid.NewGuid().ToString();

            // Act
            var context = _manager.LoadSessionContext(nonExistentId);

            // Assert
            Assert.NotNull(context);
            Assert.Equal(nonExistentId, context.SessionId);
            Assert.Empty(context.Turns);
            Assert.Null(context.CurrentWorkflow);
            Assert.Null(context.PendingRetry);
            Assert.Equal(0, context.ConsecutiveErrors);
        }

        /// <summary>
        /// Unit Test: SaveSessionContext with RetryContext
        /// Validates: Requirements 5.1, 5.2
        /// </summary>
        [Fact]
        public void SaveSessionContext_WithRetryContext_PreservesRetryData()
        {
            // Arrange
            var sessionId = Guid.NewGuid().ToString();
            var context = new SessionContext
            {
                SessionId = sessionId,
                Turns = new List<ConversationTurn>
                {
                    new ConversationTurn
                    {
                        UserMessage = "Generate code",
                        AssistantResponse = null,
                        IsError = true,
                        Timestamp = DateTime.UtcNow
                    }
                },
                PendingRetry = new RetryContext
                {
                    OriginalUserMessage = "Generate code",
                    ConversationHistory = new List<ConversationTurn>(),
                    WorkflowState = null,
                    FailedAt = DateTime.UtcNow,
                    ErrorType = "RateLimit"
                },
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 1
            };

            // Act
            _manager.SaveSessionContext(context);
            var loaded = _manager.LoadSessionContext(sessionId);

            // Assert
            Assert.NotNull(loaded.PendingRetry);
            Assert.Equal("Generate code", loaded.PendingRetry.OriginalUserMessage);
            Assert.Equal("RateLimit", loaded.PendingRetry.ErrorType);
            Assert.Equal(1, loaded.ConsecutiveErrors);
        }

        /// <summary>
        /// Property 13: 세션 컨텍스트 라운드 트립
        /// For any SessionContext, saving it to local file and then loading it should return
        /// an equivalent context with all messages, workflow state, and retry context preserved.
        /// Validates: Requirements 4.3, 5.2
        /// Feature: error-resilient-context, Property 13: 세션 컨텍스트 라운드 트립
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SessionContextRoundTrip()
        {
            return Prop.ForAll(
                GenerateSessionContext(),
                context =>
                {
                    var testPath = Path.Combine(Path.GetTempPath(), "BIBIM_Tests", Guid.NewGuid().ToString());
                    var manager = new LocalSessionManager(testPath);

                    try
                    {
                        // Save
                        manager.SaveSessionContext(context);

                        // Load
                        var loaded = manager.LoadSessionContext(context.SessionId);

                        // Verify all properties
                        var sessionIdMatches = loaded.SessionId == context.SessionId;
                        var turnsCountMatches = loaded.Turns.Count == context.Turns.Count;
                        var consecutiveErrorsMatch = loaded.ConsecutiveErrors == context.ConsecutiveErrors;

                        // Verify turns
                        var turnsMatch = true;
                        for (int i = 0; i < context.Turns.Count; i++)
                        {
                            if (loaded.Turns[i].UserMessage != context.Turns[i].UserMessage ||
                                loaded.Turns[i].AssistantResponse != context.Turns[i].AssistantResponse ||
                                loaded.Turns[i].IsError != context.Turns[i].IsError)
                            {
                                turnsMatch = false;
                                break;
                            }
                        }

                        // Verify workflow state
                        var workflowMatches = true;
                        if (context.CurrentWorkflow != null)
                        {
                            workflowMatches = loaded.CurrentWorkflow != null &&
                                loaded.CurrentWorkflow.Phase == context.CurrentWorkflow.Phase &&
                                loaded.CurrentWorkflow.DocumentPath == context.CurrentWorkflow.DocumentPath &&
                                loaded.CurrentWorkflow.PendingAction == context.CurrentWorkflow.PendingAction;
                        }
                        else
                        {
                            workflowMatches = loaded.CurrentWorkflow == null;
                        }

                        // Verify retry context
                        var retryMatches = true;
                        if (context.PendingRetry != null)
                        {
                            retryMatches = loaded.PendingRetry != null &&
                                loaded.PendingRetry.OriginalUserMessage == context.PendingRetry.OriginalUserMessage &&
                                loaded.PendingRetry.ErrorType == context.PendingRetry.ErrorType;
                        }
                        else
                        {
                            retryMatches = loaded.PendingRetry == null;
                        }

                        return sessionIdMatches && turnsCountMatches && turnsMatch && 
                               workflowMatches && retryMatches && consecutiveErrorsMatch;
                    }
                    finally
                    {
                        try { Directory.Delete(testPath, true); } catch { }
                    }
                });
        }

        /// <summary>
        /// Generator for random SessionContext instances
        /// </summary>
        private static Arbitrary<SessionContext> GenerateSessionContext()
        {
            var gen = from turnCount in Gen.Choose(0, 10)
                      from hasWorkflow in Arb.Generate<bool>()
                      from hasRetry in Arb.Generate<bool>()
                      from consecutiveErrors in Gen.Choose(0, 5)
                      select new SessionContext
                      {
                          SessionId = Guid.NewGuid().ToString(),
                          Turns = GenerateTurns(turnCount),
                          CurrentWorkflow = hasWorkflow ? GenerateWorkflowState() : null,
                          PendingRetry = hasRetry ? GenerateRetryContext() : null,
                          LastUpdated = DateTime.UtcNow,
                          ConsecutiveErrors = consecutiveErrors
                      };

            return gen.ToArbitrary();
        }

        private static List<ConversationTurn> GenerateTurns(int count)
        {
            var turns = new List<ConversationTurn>();
            for (int i = 0; i < count; i++)
            {
                turns.Add(new ConversationTurn
                {
                    UserMessage = $"User message {i}",
                    AssistantResponse = i % 3 == 0 ? null : $"Assistant response {i}",
                    IsError = i % 3 == 0,
                    Timestamp = DateTime.UtcNow.AddMinutes(-count + i)
                });
            }
            return turns;
        }

        private static WorkflowState GenerateWorkflowState()
        {
            var phases = new[] { "requirements", "design", "tasks" };
            var actions = new[] { "confirm", "approve", null };
            var random = new System.Random();

            return new WorkflowState
            {
                Phase = phases[random.Next(phases.Length)],
                DocumentPath = $".kiro/specs/feature-{random.Next(1000)}/requirements.md",
                PendingAction = actions[random.Next(actions.Length)],
                Metadata = new Dictionary<string, object> { { "key", "value" } }
            };
        }

        private static RetryContext GenerateRetryContext()
        {
            var errorTypes = new[] { "RateLimit", "Timeout", "ServiceUnavailable", "Unknown" };
            var random = new System.Random();

            return new RetryContext
            {
                OriginalUserMessage = "Original message",
                ConversationHistory = GenerateTurns(random.Next(0, 5)),
                WorkflowState = random.Next(0, 2) == 0 ? GenerateWorkflowState() : null,
                FailedAt = DateTime.UtcNow,
                ErrorType = errorTypes[random.Next(errorTypes.Length)]
            };
        }

        #endregion
    }
}
