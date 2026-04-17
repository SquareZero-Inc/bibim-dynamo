// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace BIBIM_MVP.Tests
{
    /// <summary>
    /// Unit tests for HistoryManager session context save/restore functionality
    /// Requirements: 5.1, 5.2, 4.3
    /// </summary>
    public class HistoryManagerTests
    {
        /// <summary>
        /// Test that SessionContext can be serialized and deserialized correctly
        /// This verifies the JsonHelper integration works properly
        /// </summary>
        [Fact]
        public void SessionContext_SerializationRoundTrip_PreservesAllData()
        {
            // Arrange
            var originalContext = new SessionContext
            {
                SessionId = Guid.NewGuid().ToString(),
                Turns = new List<ConversationTurn>
                {
                    new ConversationTurn
                    {
                        UserMessage = "Test user message",
                        AssistantResponse = "Test assistant response",
                        IsError = false,
                        Timestamp = DateTime.UtcNow
                    },
                    new ConversationTurn
                    {
                        UserMessage = "Second message",
                        AssistantResponse = null,
                        IsError = true,
                        Timestamp = DateTime.UtcNow
                    }
                },
                CurrentWorkflow = new WorkflowState
                {
                    Phase = "requirements",
                    DocumentPath = ".kiro/specs/test/requirements.md",
                    PendingAction = "confirm",
                    Metadata = new Dictionary<string, object>
                    {
                        { "testKey", "testValue" }
                    }
                },
                PendingRetry = new RetryContext
                {
                    OriginalUserMessage = "Retry message",
                    ConversationHistory = new List<ConversationTurn>(),
                    WorkflowState = null,
                    FailedAt = DateTime.UtcNow,
                    ErrorType = "RateLimit"
                },
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 2
            };

            // Act - Serialize and deserialize
            string json = JsonHelper.Serialize(originalContext);
            var deserializedContext = JsonHelper.Deserialize<SessionContext>(json);

            // Assert
            Assert.NotNull(deserializedContext);
            Assert.Equal(originalContext.SessionId, deserializedContext.SessionId);
            Assert.Equal(originalContext.Turns.Count, deserializedContext.Turns.Count);
            Assert.Equal(originalContext.ConsecutiveErrors, deserializedContext.ConsecutiveErrors);
            
            // Verify first turn
            Assert.Equal(originalContext.Turns[0].UserMessage, deserializedContext.Turns[0].UserMessage);
            Assert.Equal(originalContext.Turns[0].AssistantResponse, deserializedContext.Turns[0].AssistantResponse);
            Assert.Equal(originalContext.Turns[0].IsError, deserializedContext.Turns[0].IsError);
            
            // Verify second turn (with error)
            Assert.Equal(originalContext.Turns[1].UserMessage, deserializedContext.Turns[1].UserMessage);
            Assert.Null(deserializedContext.Turns[1].AssistantResponse);
            Assert.True(deserializedContext.Turns[1].IsError);
            
            // Verify workflow state
            Assert.NotNull(deserializedContext.CurrentWorkflow);
            Assert.Equal(originalContext.CurrentWorkflow.Phase, deserializedContext.CurrentWorkflow.Phase);
            Assert.Equal(originalContext.CurrentWorkflow.DocumentPath, deserializedContext.CurrentWorkflow.DocumentPath);
            Assert.Equal(originalContext.CurrentWorkflow.PendingAction, deserializedContext.CurrentWorkflow.PendingAction);
            
            // Verify retry context
            Assert.NotNull(deserializedContext.PendingRetry);
            Assert.Equal(originalContext.PendingRetry.OriginalUserMessage, deserializedContext.PendingRetry.OriginalUserMessage);
            Assert.Equal(originalContext.PendingRetry.ErrorType, deserializedContext.PendingRetry.ErrorType);
        }

        /// <summary>
        /// Test that empty SessionContext can be serialized
        /// </summary>
        [Fact]
        public void SessionContext_EmptyContext_SerializesCorrectly()
        {
            // Arrange
            var emptyContext = new SessionContext
            {
                SessionId = Guid.NewGuid().ToString(),
                Turns = new List<ConversationTurn>(),
                CurrentWorkflow = null,
                PendingRetry = null,
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 0
            };

            // Act
            string json = JsonHelper.Serialize(emptyContext);
            var deserializedContext = JsonHelper.Deserialize<SessionContext>(json);

            // Assert
            Assert.NotNull(deserializedContext);
            Assert.Equal(emptyContext.SessionId, deserializedContext.SessionId);
            Assert.Empty(deserializedContext.Turns);
            Assert.Null(deserializedContext.CurrentWorkflow);
            Assert.Null(deserializedContext.PendingRetry);
            Assert.Equal(0, deserializedContext.ConsecutiveErrors);
        }

        /// <summary>
        /// Test that SessionContext with only workflow state serializes correctly
        /// </summary>
        [Fact]
        public void SessionContext_WithWorkflowOnly_SerializesCorrectly()
        {
            // Arrange
            var context = new SessionContext
            {
                SessionId = Guid.NewGuid().ToString(),
                Turns = new List<ConversationTurn>(),
                CurrentWorkflow = new WorkflowState
                {
                    Phase = "design",
                    DocumentPath = ".kiro/specs/feature/design.md",
                    PendingAction = "approve",
                    Metadata = new Dictionary<string, object>()
                },
                PendingRetry = null,
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 0
            };

            // Act
            string json = JsonHelper.Serialize(context);
            var deserializedContext = JsonHelper.Deserialize<SessionContext>(json);

            // Assert
            Assert.NotNull(deserializedContext);
            Assert.NotNull(deserializedContext.CurrentWorkflow);
            Assert.Equal("design", deserializedContext.CurrentWorkflow.Phase);
            Assert.Equal("approve", deserializedContext.CurrentWorkflow.PendingAction);
            Assert.Null(deserializedContext.PendingRetry);
        }

        /// <summary>
        /// Test that SessionContext with only retry context serializes correctly
        /// </summary>
        [Fact]
        public void SessionContext_WithRetryOnly_SerializesCorrectly()
        {
            // Arrange
            var context = new SessionContext
            {
                SessionId = Guid.NewGuid().ToString(),
                Turns = new List<ConversationTurn>
                {
                    new ConversationTurn
                    {
                        UserMessage = "Failed message",
                        AssistantResponse = null,
                        IsError = true,
                        Timestamp = DateTime.UtcNow
                    }
                },
                CurrentWorkflow = null,
                PendingRetry = new RetryContext
                {
                    OriginalUserMessage = "Failed message",
                    ConversationHistory = new List<ConversationTurn>(),
                    WorkflowState = null,
                    FailedAt = DateTime.UtcNow,
                    ErrorType = "Timeout"
                },
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 1
            };

            // Act
            string json = JsonHelper.Serialize(context);
            var deserializedContext = JsonHelper.Deserialize<SessionContext>(json);

            // Assert
            Assert.NotNull(deserializedContext);
            Assert.Null(deserializedContext.CurrentWorkflow);
            Assert.NotNull(deserializedContext.PendingRetry);
            Assert.Equal("Timeout", deserializedContext.PendingRetry.ErrorType);
            Assert.Equal(1, deserializedContext.ConsecutiveErrors);
        }

        /// <summary>
        /// Test that multiple conversation turns are preserved in order
        /// </summary>
        [Fact]
        public void SessionContext_MultipleConversationTurns_PreservesOrder()
        {
            // Arrange
            var context = new SessionContext
            {
                SessionId = Guid.NewGuid().ToString(),
                Turns = new List<ConversationTurn>
                {
                    new ConversationTurn { UserMessage = "Message 1", AssistantResponse = "Response 1", IsError = false, Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                    new ConversationTurn { UserMessage = "Message 2", AssistantResponse = "Response 2", IsError = false, Timestamp = DateTime.UtcNow.AddMinutes(-4) },
                    new ConversationTurn { UserMessage = "Message 3", AssistantResponse = null, IsError = true, Timestamp = DateTime.UtcNow.AddMinutes(-3) },
                    new ConversationTurn { UserMessage = "Message 4", AssistantResponse = "Response 4", IsError = false, Timestamp = DateTime.UtcNow.AddMinutes(-2) }
                },
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 0
            };

            // Act
            string json = JsonHelper.Serialize(context);
            var deserializedContext = JsonHelper.Deserialize<SessionContext>(json);

            // Assert
            Assert.Equal(4, deserializedContext.Turns.Count);
            Assert.Equal("Message 1", deserializedContext.Turns[0].UserMessage);
            Assert.Equal("Response 1", deserializedContext.Turns[0].AssistantResponse);
            Assert.Equal("Message 2", deserializedContext.Turns[1].UserMessage);
            Assert.Equal("Message 3", deserializedContext.Turns[2].UserMessage);
            Assert.True(deserializedContext.Turns[2].IsError);
            Assert.Equal("Message 4", deserializedContext.Turns[3].UserMessage);
        }

        // ============================================================================
        // Session Save/Restore Tests with Mocked Supabase
        // Requirements: 5.1, 4.3
        // ============================================================================

        /// <summary>
        /// Test that SaveSessionContextAsync successfully saves a session context
        /// Requirements: 5.1
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_ValidContext_SavesSuccessfully()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            // Add some conversation turns
            contextManager.AddTurn("User message 1", "AI response 1", false);
            contextManager.AddTurn("User message 2", "AI response 2", false);

            // Act
            await contextManager.SaveSessionAsync();

            // Assert - Verify the session was saved by loading it back
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);
            Assert.NotNull(loadedContext);
            Assert.Equal(sessionId, loadedContext.SessionId);
            Assert.Equal(2, loadedContext.Turns.Count);
            Assert.Equal("User message 1", loadedContext.Turns[0].UserMessage);
            Assert.Equal("AI response 1", loadedContext.Turns[0].AssistantResponse);
        }

        /// <summary>
        /// Test that LoadSessionContextAsync successfully restores a saved session
        /// Requirements: 4.3, 5.2
        /// </summary>
        [Fact]
        public async Task LoadSessionContext_ExistingSession_RestoresSuccessfully()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager1 = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager1.StartNewSession(sessionId);
            contextManager1.AddTurn("Original message", "Original response", false);
            contextManager1.UpdateWorkflowState(new WorkflowState
            {
                Phase = "requirements",
                DocumentPath = ".kiro/specs/test/requirements.md",
                PendingAction = "confirm"
            });
            
            // Save the session
            await contextManager1.SaveSessionAsync();

            // Act - Create a new context manager and restore the session
            var contextManager2 = new ConversationContextManager(mockHistoryManager);
            var restoredContext = await contextManager2.RestoreSessionAsync(sessionId);

            // Assert
            Assert.NotNull(restoredContext);
            Assert.Equal(sessionId, restoredContext.SessionId);
            Assert.Equal(1, restoredContext.Turns.Count);
            Assert.Equal("Original message", restoredContext.Turns[0].UserMessage);
            Assert.Equal("Original response", restoredContext.Turns[0].AssistantResponse);
            Assert.NotNull(restoredContext.CurrentWorkflow);
            Assert.Equal("requirements", restoredContext.CurrentWorkflow.Phase);
            Assert.Equal(".kiro/specs/test/requirements.md", restoredContext.CurrentWorkflow.DocumentPath);
        }

        /// <summary>
        /// Test that LoadSessionContextAsync returns null for non-existent session
        /// Requirements: 4.3
        /// </summary>
        [Fact]
        public async Task LoadSessionContext_NonExistentSession_CreatesNewSession()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            var nonExistentSessionId = Guid.NewGuid().ToString();

            // Act
            var restoredContext = await contextManager.RestoreSessionAsync(nonExistentSessionId);

            // Assert - Should create a new empty session
            Assert.NotNull(restoredContext);
            Assert.Equal(nonExistentSessionId, restoredContext.SessionId);
            Assert.Empty(restoredContext.Turns);
            Assert.Null(restoredContext.CurrentWorkflow);
            Assert.Null(restoredContext.PendingRetry);
            Assert.Equal(0, restoredContext.ConsecutiveErrors);
        }

        /// <summary>
        /// Test that SaveSessionContextAsync preserves all conversation turns
        /// Requirements: 5.1, 5.2
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_WithMultipleTurns_PreservesAllTurns()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            // Add multiple turns including errors
            contextManager.AddTurn("Message 1", "Response 1", false);
            contextManager.AddTurn("Message 2", "Response 2", false);
            contextManager.AddTurn("Message 3", null, true); // Error turn
            contextManager.AddTurn("Message 4", "Response 4", false);

            // Act
            await contextManager.SaveSessionAsync();
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);

            // Assert
            Assert.NotNull(loadedContext);
            Assert.Equal(4, loadedContext.Turns.Count);
            
            // Verify first turn
            Assert.Equal("Message 1", loadedContext.Turns[0].UserMessage);
            Assert.Equal("Response 1", loadedContext.Turns[0].AssistantResponse);
            Assert.False(loadedContext.Turns[0].IsError);
            
            // Verify error turn
            Assert.Equal("Message 3", loadedContext.Turns[2].UserMessage);
            Assert.Null(loadedContext.Turns[2].AssistantResponse);
            Assert.True(loadedContext.Turns[2].IsError);
            
            // Verify last turn
            Assert.Equal("Message 4", loadedContext.Turns[3].UserMessage);
            Assert.Equal("Response 4", loadedContext.Turns[3].AssistantResponse);
        }

        /// <summary>
        /// Test that SaveSessionContextAsync preserves workflow state
        /// Requirements: 5.1, 5.3
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_WithWorkflowState_PreservesWorkflow()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            var workflowState = new WorkflowState
            {
                Phase = "design",
                DocumentPath = ".kiro/specs/feature-x/design.md",
                PendingAction = "approve",
                Metadata = new Dictionary<string, object>
                {
                    { "key1", "value1" },
                    { "key2", 42 }
                }
            };
            
            contextManager.UpdateWorkflowState(workflowState);
            contextManager.AddTurn("Test message", "Test response", false);

            // Act
            await contextManager.SaveSessionAsync();
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);

            // Assert
            Assert.NotNull(loadedContext);
            Assert.NotNull(loadedContext.CurrentWorkflow);
            Assert.Equal("design", loadedContext.CurrentWorkflow.Phase);
            Assert.Equal(".kiro/specs/feature-x/design.md", loadedContext.CurrentWorkflow.DocumentPath);
            Assert.Equal("approve", loadedContext.CurrentWorkflow.PendingAction);
            Assert.Equal(2, loadedContext.CurrentWorkflow.Metadata.Count);
        }

        /// <summary>
        /// Test that SaveSessionContextAsync preserves retry context
        /// Requirements: 5.1, 5.2
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_WithRetryContext_PreservesRetry()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            // Add some conversation history
            contextManager.AddTurn("Message 1", "Response 1", false);
            contextManager.AddTurn("Message 2", "Response 2", false);
            
            // Create retry context
            var retryContext = contextManager.CreateRetryContext("Failed message", "RateLimit");

            // Act
            await contextManager.SaveSessionAsync();
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);

            // Assert
            Assert.NotNull(loadedContext);
            Assert.NotNull(loadedContext.PendingRetry);
            Assert.Equal("Failed message", loadedContext.PendingRetry.OriginalUserMessage);
            Assert.Equal("RateLimit", loadedContext.PendingRetry.ErrorType);
            Assert.Equal(2, loadedContext.PendingRetry.ConversationHistory.Count);
        }

        /// <summary>
        /// Test that SaveSessionContextAsync preserves consecutive error count
        /// Requirements: 5.1, 7.4
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_WithConsecutiveErrors_PreservesErrorCount()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            // Add error turns to increment consecutive error count
            contextManager.AddTurn("Message 1", null, true);
            contextManager.AddTurn("Message 2", null, true);
            contextManager.AddTurn("Message 3", null, true);

            // Act
            await contextManager.SaveSessionAsync();
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);

            // Assert
            Assert.NotNull(loadedContext);
            Assert.Equal(3, loadedContext.ConsecutiveErrors);
            Assert.Equal(3, loadedContext.Turns.Count);
            Assert.True(loadedContext.Turns.All(t => t.IsError));
        }

        /// <summary>
        /// Test that multiple save operations update the same session
        /// Requirements: 5.1
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_MultipleSaves_UpdatesSameSession()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            // First save
            contextManager.AddTurn("Message 1", "Response 1", false);
            await contextManager.SaveSessionAsync();
            
            // Second save with additional turn
            contextManager.AddTurn("Message 2", "Response 2", false);
            await contextManager.SaveSessionAsync();

            // Act
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);

            // Assert - Should have both turns
            Assert.NotNull(loadedContext);
            Assert.Equal(2, loadedContext.Turns.Count);
            Assert.Equal("Message 1", loadedContext.Turns[0].UserMessage);
            Assert.Equal("Message 2", loadedContext.Turns[1].UserMessage);
        }

        /// <summary>
        /// Test that session context with empty turns can be saved and restored
        /// Requirements: 5.1, 4.3
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_EmptyTurns_SavesAndRestoresSuccessfully()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            // Don't add any turns - save empty session

            // Act
            await contextManager.SaveSessionAsync();
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);

            // Assert
            Assert.NotNull(loadedContext);
            Assert.Equal(sessionId, loadedContext.SessionId);
            Assert.Empty(loadedContext.Turns);
            Assert.Null(loadedContext.CurrentWorkflow);
            Assert.Null(loadedContext.PendingRetry);
            Assert.Equal(0, loadedContext.ConsecutiveErrors);
        }

        /// <summary>
        /// Test that session context with workflow but no retry can be saved
        /// Requirements: 5.1, 5.3
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_WorkflowWithoutRetry_SavesSuccessfully()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            contextManager.UpdateWorkflowState(new WorkflowState
            {
                Phase = "tasks",
                DocumentPath = ".kiro/specs/test/tasks.md",
                PendingAction = null
            });

            // Act
            await contextManager.SaveSessionAsync();
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);

            // Assert
            Assert.NotNull(loadedContext);
            Assert.NotNull(loadedContext.CurrentWorkflow);
            Assert.Equal("tasks", loadedContext.CurrentWorkflow.Phase);
            Assert.Null(loadedContext.PendingRetry);
        }

        /// <summary>
        /// Test that session context with retry but no workflow can be saved
        /// Requirements: 5.1
        /// </summary>
        [Fact]
        public async Task SaveSessionContext_RetryWithoutWorkflow_SavesSuccessfully()
        {
            // Arrange
            var mockHistoryManager = new MockHistoryManager();
            var contextManager = new ConversationContextManager(mockHistoryManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            contextManager.AddTurn("Message 1", "Response 1", false);
            contextManager.CreateRetryContext("Failed message", "Timeout");

            // Act
            await contextManager.SaveSessionAsync();
            var loadedContext = await mockHistoryManager.LoadSessionContextAsync(sessionId);

            // Assert
            Assert.NotNull(loadedContext);
            Assert.Null(loadedContext.CurrentWorkflow);
            Assert.NotNull(loadedContext.PendingRetry);
            Assert.Equal("Timeout", loadedContext.PendingRetry.ErrorType);
        }
    }
}
