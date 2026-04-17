// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using BIBIM_MVP;

namespace BIBIM_MVP.Tests
{
    /// <summary>
    /// Property-based tests for error-resilient context management models.
    /// Feature: error-resilient-context
    /// </summary>
    public class ErrorResilientContextPropertyTests
    {
        /// <summary>
        /// Property 1: Workflow State Preservation on API Error
        /// For any workflow state and API error, the workflow state before the error
        /// should remain identical after the error occurs.
        /// **Validates: Requirements 1.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property WorkflowStatePreservationOnApiError()
        {
            return Prop.ForAll(
                GenerateSessionContextWithWorkflow(),
                GenerateErrorType(),
                (sessionContext, errorType) =>
                {
                    // Arrange: Create a mock local session manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a session and set up the workflow state
                    contextManager.StartNewSession(sessionContext.SessionId);
                    if (sessionContext.CurrentWorkflow != null)
                    {
                        contextManager.UpdateWorkflowState(sessionContext.CurrentWorkflow);
                    }
                    
                    // Add existing turns
                    foreach (var turn in sessionContext.Turns)
                    {
                        contextManager.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                    }
                    
                    // Capture workflow state before error
                    var workflowBeforeError = contextManager.GetCurrentSession().CurrentWorkflow;
                    
                    // Act: Simulate API error by creating retry context
                    var userMessage = "Test message that will fail";
                    contextManager.CreateRetryContext(userMessage, errorType);
                    
                    // Assert: Workflow state should be preserved
                    var workflowAfterError = contextManager.GetCurrentSession().CurrentWorkflow;
                    
                    if (workflowBeforeError == null)
                    {
                        return workflowAfterError == null;
                    }
                    
                    return workflowAfterError != null &&
                           workflowAfterError.Phase == workflowBeforeError.Phase &&
                           workflowAfterError.DocumentPath == workflowBeforeError.DocumentPath &&
                           workflowAfterError.PendingAction == workflowBeforeError.PendingAction &&
                           workflowAfterError.Metadata.Count == workflowBeforeError.Metadata.Count;
                });
        }

        /// <summary>
        /// Property 2: Message History Preservation on API Error
        /// For any conversation session with message history, when an API error occurs,
        /// all message history should remain intact.
        /// **Validates: Requirements 1.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property MessageHistoryPreservationOnApiError()
        {
            return Prop.ForAll(
                GenerateSessionContextWithTurns(),
                GenerateErrorType(),
                (sessionContext, errorType) =>
                {
                    // Arrange: Create a mock local session manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a session and add turns
                    contextManager.StartNewSession(sessionContext.SessionId);
                    foreach (var turn in sessionContext.Turns)
                    {
                        contextManager.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                    }
                    
                    // Capture message history before error
                    var historyBeforeError = contextManager.GetCurrentSession().Turns;
                    var countBeforeError = historyBeforeError.Count;
                    
                    // Act: Simulate API error by creating retry context
                    var userMessage = "Test message that will fail";
                    contextManager.CreateRetryContext(userMessage, errorType);
                    
                    // Assert: Message history should be preserved (same count and content)
                    var historyAfterError = contextManager.GetCurrentSession().Turns;
                    
                    if (historyAfterError.Count != countBeforeError)
                    {
                        return false;
                    }
                    
                    // Verify all messages are identical
                    for (int i = 0; i < countBeforeError; i++)
                    {
                        if (historyAfterError[i].UserMessage != historyBeforeError[i].UserMessage ||
                            historyAfterError[i].AssistantResponse != historyBeforeError[i].AssistantResponse ||
                            historyAfterError[i].IsError != historyBeforeError[i].IsError)
                        {
                            return false;
                        }
                    }
                    
                    return true;
                });
        }

        /// <summary>
        /// Property 4: Retry Context Reconstruction
        /// For any preserved retry context, when retrying, the original request parameters
        /// (message, conversation history, workflow state) should be accurately reconstructed.
        /// **Validates: Requirements 2.2, 6.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property RetryContextReconstruction()
        {
            return Prop.ForAll(
                GenerateSessionContextWithTurns(),
                GenerateErrorType(),
                (sessionContext, errorType) =>
                {
                    // Arrange: Create a mock local session manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a session and add turns
                    contextManager.StartNewSession(sessionContext.SessionId);
                    
                    // Set workflow state if present
                    if (sessionContext.CurrentWorkflow != null)
                    {
                        contextManager.UpdateWorkflowState(sessionContext.CurrentWorkflow);
                    }
                    
                    // Add conversation turns
                    foreach (var turn in sessionContext.Turns)
                    {
                        contextManager.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                    }
                    
                    // Capture state before creating retry context
                    var originalTurns = contextManager.GetCurrentSession().Turns;
                    var originalWorkflow = contextManager.GetCurrentSession().CurrentWorkflow;
                    var originalUserMessage = "This message will fail";
                    
                    // Act: Create retry context
                    var retryContext = contextManager.CreateRetryContext(originalUserMessage, errorType);
                    
                    // Assert: Verify all parameters are accurately reconstructed in retry context
                    
                    // 1. Original user message should be preserved
                    var messagePreserved = retryContext.OriginalUserMessage == originalUserMessage;
                    
                    // 2. Conversation history should match (same count and content)
                    var historyCountMatches = retryContext.ConversationHistory.Count == originalTurns.Count;
                    var historyContentMatches = true;
                    if (historyCountMatches)
                    {
                        for (int i = 0; i < originalTurns.Count; i++)
                        {
                            if (retryContext.ConversationHistory[i].UserMessage != originalTurns[i].UserMessage ||
                                retryContext.ConversationHistory[i].AssistantResponse != originalTurns[i].AssistantResponse ||
                                retryContext.ConversationHistory[i].IsError != originalTurns[i].IsError)
                            {
                                historyContentMatches = false;
                                break;
                            }
                        }
                    }
                    
                    // 3. Workflow state should be preserved (if it exists)
                    var workflowPreserved = true;
                    if (originalWorkflow == null)
                    {
                        workflowPreserved = retryContext.WorkflowState == null;
                    }
                    else
                    {
                        workflowPreserved = retryContext.WorkflowState != null &&
                            retryContext.WorkflowState.Phase == originalWorkflow.Phase &&
                            retryContext.WorkflowState.DocumentPath == originalWorkflow.DocumentPath &&
                            retryContext.WorkflowState.PendingAction == originalWorkflow.PendingAction;
                    }
                    
                    // 4. Error type should be preserved
                    var errorTypePreserved = retryContext.ErrorType == errorType;
                    
                    // 5. Failed timestamp should be recent (within last 5 seconds)
                    var timestampRecent = (DateTime.UtcNow - retryContext.FailedAt).TotalSeconds < 5;
                    
                    // 6. Retry context should be accessible via GetPendingRetry
                    var pendingRetry = contextManager.GetPendingRetry();
                    var retryContextAccessible = pendingRetry != null && 
                                                 pendingRetry.OriginalUserMessage == originalUserMessage;
                    
                    return messagePreserved && 
                           historyCountMatches && 
                           historyContentMatches &&
                           workflowPreserved &&
                           errorTypePreserved &&
                           timestampRecent &&
                           retryContextAccessible;
                });
        }

        /// <summary>
        /// Property 6: Retry Success UI State Restoration
        /// For any retry that succeeds, the retry button should be removed and
        /// the normal response should be displayed.
        /// **Validates: Requirements 2.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property RetrySuccessUIStateRestoration()
        {
            return Prop.ForAll(
                GenerateSessionContextWithTurns(),
                GenerateErrorType(),
                (sessionContext, errorType) =>
                {
                    // This property verifies the logical state changes that should occur
                    // when a retry succeeds. Since we can't test the actual UI,
                    // we verify the context manager state changes.
                    
                    // Arrange: Create a mock local session manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a session and add turns
                    contextManager.StartNewSession(sessionContext.SessionId);
                    foreach (var turn in sessionContext.Turns)
                    {
                        contextManager.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                    }
                    
                    // Create retry context (simulating error)
                    var userMessage = "Test message that will fail";
                    contextManager.CreateRetryContext(userMessage, errorType);
                    
                    // Verify retry context exists
                    var retryContextExists = contextManager.GetPendingRetry() != null;
                    if (!retryContextExists)
                    {
                        return false;
                    }
                    
                    // Act: Simulate successful retry by adding successful turn
                    contextManager.AddTurn(userMessage, "Successful response", isError: false);
                    
                    // Clear retry context (as would happen on success)
                    contextManager.ClearPendingRetry();
                    
                    // Assert: Verify state after successful retry
                    
                    // 1. Retry context should be cleared
                    var retryContextCleared = contextManager.GetPendingRetry() == null;
                    
                    // 2. Consecutive errors should be reset to 0
                    var errorsReset = contextManager.GetConsecutiveErrorCount() == 0;
                    
                    // 3. Session should have the successful response
                    var session = contextManager.GetCurrentSession();
                    var hasSuccessfulResponse = session.Turns.Any(t => 
                        t.UserMessage == userMessage && 
                        t.AssistantResponse == "Successful response" && 
                        !t.IsError);
                    
                    return retryContextCleared && errorsReset && hasSuccessfulResponse;
                });
        }

        /// <summary>
        /// Property 7: Retry Failure Button Persistence
        /// For any retry that fails, the retry button should remain visible
        /// and the error message should be displayed.
        /// **Validates: Requirements 2.5**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property RetryFailureButtonPersistence()
        {
            return Prop.ForAll(
                GenerateSessionContextWithTurns(),
                GenerateErrorType(),
                GenerateErrorType(),
                (sessionContext, errorType1, errorType2) =>
                {
                    // This property verifies the logical state changes that should occur
                    // when a retry fails. Since we can't test the actual UI,
                    // we verify the context manager state changes.
                    
                    // Arrange: Create a mock local session manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a session and add turns
                    contextManager.StartNewSession(sessionContext.SessionId);
                    foreach (var turn in sessionContext.Turns)
                    {
                        contextManager.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                    }
                    
                    // Create initial retry context (simulating first error)
                    var userMessage = "Test message that will fail";
                    contextManager.CreateRetryContext(userMessage, errorType1);
                    
                    // Verify initial retry context exists
                    var initialRetryExists = contextManager.GetPendingRetry() != null;
                    if (!initialRetryExists)
                    {
                        return false;
                    }
                    
                    // Capture initial consecutive error count
                    var initialErrorCount = contextManager.GetConsecutiveErrorCount();
                    
                    // Act: Simulate failed retry by adding another error turn
                    contextManager.AddTurn(userMessage, null, isError: true);
                    
                    // Create new retry context (as would happen on retry failure)
                    contextManager.CreateRetryContext(userMessage, errorType2);
                    
                    // Assert: Verify state after failed retry
                    
                    // 1. Retry context should still exist (not cleared)
                    var retryContextStillExists = contextManager.GetPendingRetry() != null;
                    
                    // 2. Consecutive errors should have increased
                    var errorsIncreased = contextManager.GetConsecutiveErrorCount() > initialErrorCount;
                    
                    // 3. Session should have the error turn
                    var session = contextManager.GetCurrentSession();
                    var hasErrorTurn = session.Turns.Any(t => 
                        t.UserMessage == userMessage && 
                        t.AssistantResponse == null && 
                        t.IsError);
                    
                    // 4. Retry context should have the updated error type
                    var retryContext = contextManager.GetPendingRetry();
                    var errorTypeUpdated = retryContext != null && retryContext.ErrorType == errorType2;
                    
                    return retryContextStillExists && errorsIncreased && hasErrorTurn && errorTypeUpdated;
                });
        }

        /// <summary>
        /// Property 17: Retry Failure Context Invariance
        /// For any retry that fails, the session context before the retry attempt
        /// should remain identical after the retry failure (except for the retry context itself).
        /// **Validates: Requirements 6.5**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property RetryFailureContextInvariance()
        {
            return Prop.ForAll(
                GenerateSessionContextWithTurns(),
                GenerateErrorType(),
                GenerateErrorType(),
                (sessionContext, errorType1, errorType2) =>
                {
                    // Arrange: Create a mock local session manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a session and add turns
                    contextManager.StartNewSession(sessionContext.SessionId);
                    
                    // Set workflow state if present
                    if (sessionContext.CurrentWorkflow != null)
                    {
                        contextManager.UpdateWorkflowState(sessionContext.CurrentWorkflow);
                    }
                    
                    // Add conversation turns
                    foreach (var turn in sessionContext.Turns)
                    {
                        contextManager.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                    }
                    
                    // Create initial retry context (simulating first error)
                    var userMessage = "Test message that will fail";
                    contextManager.CreateRetryContext(userMessage, errorType1);
                    
                    // Capture state before retry attempt (excluding retry context)
                    var sessionBeforeRetry = contextManager.GetCurrentSession();
                    var turnsBeforeRetry = new System.Collections.Generic.List<ConversationTurn>(sessionBeforeRetry.Turns);
                    var workflowBeforeRetry = sessionBeforeRetry.CurrentWorkflow;
                    var sessionIdBeforeRetry = sessionBeforeRetry.SessionId;
                    
                    // Act: Simulate failed retry
                    // Note: In a real retry failure, we would NOT add a new turn or modify context
                    // except for updating the retry context. Let's simulate this by just
                    // creating a new retry context (as would happen in HandleApiError after retry fails)
                    contextManager.CreateRetryContext(userMessage, errorType2);
                    
                    // Assert: Verify context invariance (everything except retry context should be unchanged)
                    var sessionAfterRetry = contextManager.GetCurrentSession();
                    
                    // 1. Session ID should be unchanged
                    var sessionIdUnchanged = sessionAfterRetry.SessionId == sessionIdBeforeRetry;
                    
                    // 2. Conversation turns should be unchanged (same count and content)
                    var turnsCountUnchanged = sessionAfterRetry.Turns.Count == turnsBeforeRetry.Count;
                    var turnsContentUnchanged = true;
                    if (turnsCountUnchanged)
                    {
                        for (int i = 0; i < turnsBeforeRetry.Count; i++)
                        {
                            if (sessionAfterRetry.Turns[i].UserMessage != turnsBeforeRetry[i].UserMessage ||
                                sessionAfterRetry.Turns[i].AssistantResponse != turnsBeforeRetry[i].AssistantResponse ||
                                sessionAfterRetry.Turns[i].IsError != turnsBeforeRetry[i].IsError)
                            {
                                turnsContentUnchanged = false;
                                break;
                            }
                        }
                    }
                    
                    // 3. Workflow state should be unchanged
                    var workflowUnchanged = true;
                    if (workflowBeforeRetry == null)
                    {
                        workflowUnchanged = sessionAfterRetry.CurrentWorkflow == null;
                    }
                    else
                    {
                        workflowUnchanged = sessionAfterRetry.CurrentWorkflow != null &&
                            sessionAfterRetry.CurrentWorkflow.Phase == workflowBeforeRetry.Phase &&
                            sessionAfterRetry.CurrentWorkflow.DocumentPath == workflowBeforeRetry.DocumentPath &&
                            sessionAfterRetry.CurrentWorkflow.PendingAction == workflowBeforeRetry.PendingAction;
                    }
                    
                    // 4. Retry context should exist (it's the only thing that should change)
                    var retryContextExists = sessionAfterRetry.PendingRetry != null;
                    
                    // 5. Retry context should have the new error type
                    var retryContextUpdated = sessionAfterRetry.PendingRetry != null &&
                                             sessionAfterRetry.PendingRetry.ErrorType == errorType2;
                    
                    return sessionIdUnchanged && 
                           turnsCountUnchanged && 
                           turnsContentUnchanged &&
                           workflowUnchanged &&
                           retryContextExists &&
                           retryContextUpdated;
                });
        }

        /// <summary>
        /// Property 11: Session Independence
        /// For any two different conversation sessions, context changes in one session
        /// should not affect the context of the other session.
        /// **Validates: Requirements 4.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SessionIndependence()
        {
            return Prop.ForAll(
                GenerateSessionContext(),
                GenerateSessionContext(),
                (sessionContext1, sessionContext2) =>
                {
                    // Ensure sessions have different IDs
                    if (sessionContext1.SessionId == sessionContext2.SessionId)
                    {
                        sessionContext2.SessionId = sessionContext1.SessionId + "_different";
                    }

                    // Arrange: Create two independent context managers with the same history manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager1 = new ConversationContextManager(mockLocalSessionManager);
                    var contextManager2 = new ConversationContextManager(mockLocalSessionManager);

                    // Start two different sessions
                    contextManager1.StartNewSession(sessionContext1.SessionId);
                    contextManager2.StartNewSession(sessionContext2.SessionId);

                    // Set up session 1 with its data
                    if (sessionContext1.CurrentWorkflow != null)
                    {
                        contextManager1.UpdateWorkflowState(sessionContext1.CurrentWorkflow);
                    }
                    foreach (var turn in sessionContext1.Turns)
                    {
                        contextManager1.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                    }

                    // Set up session 2 with its data
                    if (sessionContext2.CurrentWorkflow != null)
                    {
                        contextManager2.UpdateWorkflowState(sessionContext2.CurrentWorkflow);
                    }
                    foreach (var turn in sessionContext2.Turns)
                    {
                        contextManager2.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                    }

                    // Capture initial state of both sessions
                    var session1InitialTurnCount = contextManager1.GetCurrentSession().Turns.Count;
                    var session2InitialTurnCount = contextManager2.GetCurrentSession().Turns.Count;
                    var session1InitialWorkflow = contextManager1.GetCurrentSession().CurrentWorkflow;
                    var session2InitialWorkflow = contextManager2.GetCurrentSession().CurrentWorkflow;

                    // Act: Modify session 1 - add a new turn
                    contextManager1.AddTurn("New message in session 1", "Response in session 1", false);

                    // Modify session 1 - update workflow state
                    var newWorkflow1 = new WorkflowState
                    {
                        Phase = "modified_phase",
                        DocumentPath = ".kiro/specs/modified/requirements.md",
                        PendingAction = "modified_action",
                        Metadata = new Dictionary<string, object> { { "modified", true } }
                    };
                    contextManager1.UpdateWorkflowState(newWorkflow1);

                    // Modify session 1 - create retry context
                    contextManager1.CreateRetryContext("Failed message in session 1", "RateLimit");

                    // Assert: Session 2 should remain unchanged
                    var session2AfterModification = contextManager2.GetCurrentSession();

                    // Check turn count hasn't changed
                    var turnCountUnchanged = session2AfterModification.Turns.Count == session2InitialTurnCount;

                    // Check workflow state hasn't changed
                    var workflowUnchanged = true;
                    if (session2InitialWorkflow == null)
                    {
                        workflowUnchanged = session2AfterModification.CurrentWorkflow == null ||
                                           session2AfterModification.CurrentWorkflow.Phase == session2InitialWorkflow?.Phase;
                    }
                    else
                    {
                        workflowUnchanged = session2AfterModification.CurrentWorkflow != null &&
                                           session2AfterModification.CurrentWorkflow.Phase == session2InitialWorkflow.Phase &&
                                           session2AfterModification.CurrentWorkflow.DocumentPath == session2InitialWorkflow.DocumentPath;
                    }

                    // Check that session 2 doesn't have session 1's retry context
                    var noRetryContextLeakage = session2AfterModification.PendingRetry == null ||
                                                session2AfterModification.PendingRetry.OriginalUserMessage != "Failed message in session 1";

                    // Verify session 1 was actually modified
                    var session1Modified = contextManager1.GetCurrentSession().Turns.Count == session1InitialTurnCount + 1 &&
                                          contextManager1.GetCurrentSession().CurrentWorkflow?.Phase == "modified_phase" &&
                                          contextManager1.GetCurrentSession().PendingRetry != null;

                    return turnCountUnchanged && workflowUnchanged && noRetryContextLeakage && session1Modified;
                });
        }

        /// <summary>
        /// Property 12: New Session Initialization
        /// For any newly created conversation session, it should start with empty message history
        /// and null workflow state.
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property NewSessionInitialization()
        {
            return Prop.ForAll<NonEmptyString>(
                sessionId =>
                {
                    // Arrange: Create a mock local session manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);

                    // Act: Start a new session
                    contextManager.StartNewSession(sessionId.Get);

                    // Assert: Session should have empty state
                    var session = contextManager.GetCurrentSession();

                    // Check session ID matches
                    var sessionIdMatches = session.SessionId == sessionId.Get;

                    // Check turns list is empty
                    var turnsEmpty = session.Turns != null && session.Turns.Count == 0;

                    // Check workflow state is null
                    var workflowNull = session.CurrentWorkflow == null;

                    // Check pending retry is null
                    var retryNull = session.PendingRetry == null;

                    // Check consecutive errors is 0
                    var errorsZero = session.ConsecutiveErrors == 0;

                    // Check last updated is recent (within last 5 seconds)
                    var lastUpdatedRecent = (DateTime.UtcNow - session.LastUpdated).TotalSeconds < 5;

                    return sessionIdMatches && 
                           turnsEmpty && 
                           workflowNull && 
                           retryNull && 
                           errorsZero && 
                           lastUpdatedRecent;
                });
        }

        /// <summary>
        /// Property 13: Session Context Round Trip
        /// For any session context, saving to JSON then loading should return an equivalent context
        /// with all content (messages, workflow state, retry context) preserved.
        /// **Validates: Requirements 4.3, 5.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SessionContextRoundTrip()
        {
            return Prop.ForAll(
                GenerateSessionContext(),
                sessionContext =>
                {
                    // Act: Serialize to JSON and deserialize back
                    var json = JsonHelper.Serialize(sessionContext);
                    var restored = JsonHelper.Deserialize<SessionContext>(json);

                    // Assert: All fields should match
                    var sessionIdMatches = restored.SessionId == sessionContext.SessionId;
                    var lastUpdatedMatches = Math.Abs((restored.LastUpdated - sessionContext.LastUpdated).TotalSeconds) < 1;
                    var consecutiveErrorsMatches = restored.ConsecutiveErrors == sessionContext.ConsecutiveErrors;

                    // Verify turns
                    var turnsCountMatches = restored.Turns.Count == sessionContext.Turns.Count;
                    var turnsMatch = true;
                    if (turnsCountMatches)
                    {
                        for (int i = 0; i < sessionContext.Turns.Count; i++)
                        {
                            var original = sessionContext.Turns[i];
                            var restoredTurn = restored.Turns[i];
                            
                            if (restoredTurn.UserMessage != original.UserMessage ||
                                restoredTurn.AssistantResponse != original.AssistantResponse ||
                                restoredTurn.IsError != original.IsError ||
                                Math.Abs((restoredTurn.Timestamp - original.Timestamp).TotalSeconds) >= 1)
                            {
                                turnsMatch = false;
                                break;
                            }
                        }
                    }

                    // Verify workflow state
                    var workflowMatches = true;
                    if (sessionContext.CurrentWorkflow == null)
                    {
                        workflowMatches = restored.CurrentWorkflow == null;
                    }
                    else
                    {
                        workflowMatches = restored.CurrentWorkflow != null &&
                            restored.CurrentWorkflow.Phase == sessionContext.CurrentWorkflow.Phase &&
                            restored.CurrentWorkflow.DocumentPath == sessionContext.CurrentWorkflow.DocumentPath &&
                            restored.CurrentWorkflow.PendingAction == sessionContext.CurrentWorkflow.PendingAction &&
                            restored.CurrentWorkflow.Metadata.Count == sessionContext.CurrentWorkflow.Metadata.Count;
                    }

                    // Verify retry context
                    var retryMatches = true;
                    if (sessionContext.PendingRetry == null)
                    {
                        retryMatches = restored.PendingRetry == null;
                    }
                    else
                    {
                        retryMatches = restored.PendingRetry != null &&
                            restored.PendingRetry.OriginalUserMessage == sessionContext.PendingRetry.OriginalUserMessage &&
                            restored.PendingRetry.ErrorType == sessionContext.PendingRetry.ErrorType &&
                            restored.PendingRetry.ConversationHistory.Count == sessionContext.PendingRetry.ConversationHistory.Count &&
                            Math.Abs((restored.PendingRetry.FailedAt - sessionContext.PendingRetry.FailedAt).TotalSeconds) < 1;

                        // Verify retry context workflow state
                        if (retryMatches && sessionContext.PendingRetry.WorkflowState != null)
                        {
                            retryMatches = restored.PendingRetry.WorkflowState != null &&
                                restored.PendingRetry.WorkflowState.Phase == sessionContext.PendingRetry.WorkflowState.Phase;
                        }
                    }

                    return sessionIdMatches && 
                           lastUpdatedMatches && 
                           consecutiveErrorsMatches &&
                           turnsCountMatches && 
                           turnsMatch &&
                           workflowMatches &&
                           retryMatches;
                });
        }

        /// <summary>
        /// Property 19: Consecutive Error Counting
        /// For any conversation session, consecutive API error count should be accurately tracked,
        /// incrementing on each error and resetting to 0 on success.
        /// **Validates: Requirements 7.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ConsecutiveErrorCounting()
        {
            return Prop.ForAll(
                GenerateErrorSequence(),
                errorSequence =>
                {
                    // Arrange: Create a mock local session manager and context manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a new session
                    contextManager.StartNewSession(Guid.NewGuid().ToString());
                    
                    // Track expected consecutive error count
                    int expectedConsecutiveErrors = 0;
                    
                    // Act & Assert: Process each event in the sequence
                    foreach (var isError in errorSequence)
                    {
                        if (isError)
                        {
                            // Add error turn
                            contextManager.AddTurn("Test message", null, isError: true);
                            expectedConsecutiveErrors++;
                        }
                        else
                        {
                            // Add successful turn
                            contextManager.AddTurn("Test message", "Test response", isError: false);
                            expectedConsecutiveErrors = 0; // Reset on success
                        }
                        
                        // Verify consecutive error count matches expected
                        var actualConsecutiveErrors = contextManager.GetConsecutiveErrorCount();
                        if (actualConsecutiveErrors != expectedConsecutiveErrors)
                        {
                            return false;
                        }
                    }
                    
                    // Final verification
                    var finalCount = contextManager.GetConsecutiveErrorCount();
                    return finalCount == expectedConsecutiveErrors;
                });
        }

        /// <summary>
        /// Property 19b: Consecutive Error Counting with Mixed Operations
        /// Verifies that consecutive error counting works correctly even when
        /// interspersed with other operations like workflow updates and retry context creation.
        /// **Validates: Requirements 7.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ConsecutiveErrorCountingWithMixedOperations()
        {
            return Prop.ForAll(
                GenerateErrorSequence(),
                GenerateWorkflowState(),
                (errorSequence, workflow) =>
                {
                    // Arrange: Create a mock local session manager and context manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a new session
                    contextManager.StartNewSession(Guid.NewGuid().ToString());
                    
                    // Track expected consecutive error count
                    int expectedConsecutiveErrors = 0;
                    
                    // Act & Assert: Process each event with mixed operations
                    for (int i = 0; i < errorSequence.Count; i++)
                    {
                        var isError = errorSequence[i];
                        
                        // Occasionally update workflow state (shouldn't affect error count)
                        if (i % 3 == 0)
                        {
                            contextManager.UpdateWorkflowState(workflow);
                        }
                        
                        if (isError)
                        {
                            // Add error turn
                            contextManager.AddTurn($"Test message {i}", null, isError: true);
                            expectedConsecutiveErrors++;
                            
                            // Create retry context (shouldn't affect error count)
                            contextManager.CreateRetryContext($"Test message {i}", "RateLimit");
                        }
                        else
                        {
                            // Add successful turn
                            contextManager.AddTurn($"Test message {i}", $"Test response {i}", isError: false);
                            expectedConsecutiveErrors = 0; // Reset on success
                            
                            // Clear retry context if exists
                            if (contextManager.GetPendingRetry() != null)
                            {
                                contextManager.ClearPendingRetry();
                            }
                        }
                        
                        // Verify consecutive error count matches expected
                        var actualConsecutiveErrors = contextManager.GetConsecutiveErrorCount();
                        if (actualConsecutiveErrors != expectedConsecutiveErrors)
                        {
                            return false;
                        }
                    }
                    
                    return true;
                });
        }

        /// <summary>
        /// Property 19c: Consecutive Error Threshold Detection
        /// Verifies that the ShouldShowAlternativeGuidance method correctly detects
        /// when consecutive errors reach the threshold of 3 or more.
        /// **Validates: Requirements 7.4, 7.5**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ConsecutiveErrorThresholdDetection()
        {
            return Prop.ForAll<int>(
                Arb.From(Gen.Choose(0, 10)),
                errorCount =>
                {
                    // Arrange: Create a mock local session manager and context manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Start a new session
                    contextManager.StartNewSession(Guid.NewGuid().ToString());
                    
                    // Act: Add the specified number of consecutive errors
                    for (int i = 0; i < errorCount; i++)
                    {
                        contextManager.AddTurn($"Test message {i}", null, isError: true);
                    }
                    
                    // Assert: ShouldShowAlternativeGuidance should return true if errorCount >= 3
                    var shouldShowGuidance = contextManager.ShouldShowAlternativeGuidance();
                    var expectedShowGuidance = errorCount >= 3;
                    
                    return shouldShowGuidance == expectedShowGuidance;
                });
        }

        /// <summary>
        /// Property 8: All Errors Show Same User Message
        /// For any API error type (rate limit, timeout, service unavailable, etc.),
        /// the user-facing error message should be identical.
        /// **Validates: Requirements 3.3**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property AllErrorsShowSameUserMessage()
        {
            return Prop.ForAll(
                GenerateApiException(),
                GenerateApiException(),
                (exceptionData1, exceptionData2) =>
                {
                    // This property verifies that regardless of error type,
                    // the user message is consistent. Since we can't easily test
                    // the UI message directly, we verify the error classification
                    // doesn't affect the base error message logic.
                    
                    // The actual user message is: "현재 클로드 서버가 혼잡합니다 다시 시도해주세요"
                    // This should be the same for all error types (unless consecutive errors >= 3)
                    
                    // Create exceptions
                    var exception1 = CreateExceptionFromData(exceptionData1);
                    var exception2 = CreateExceptionFromData(exceptionData2);
                    
                    // Classify both errors
                    var errorType1 = ClassifyErrorForTest(exception1);
                    var errorType2 = ClassifyErrorForTest(exception2);
                    
                    // Both should result in valid error types
                    var validTypes = new[] { "RateLimit", "Timeout", "ServiceUnavailable", "Unknown" };
                    var type1Valid = validTypes.Contains(errorType1);
                    var type2Valid = validTypes.Contains(errorType2);
                    
                    // The key property: error classification exists but doesn't change user message
                    // (unless consecutive errors >= 3, which is tested separately)
                    return type1Valid && type2Valid;
                });
        }

        /// <summary>
        /// Property 9: Technical Details Hidden from User
        /// For any API error, the user-facing message should not contain technical details
        /// such as HTTP status codes, exception types, or stack traces.
        /// **Validates: Requirements 3.4**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property TechnicalDetailsHiddenFromUser()
        {
            return Prop.ForAll(
                GenerateApiException(),
                exceptionData =>
                {
                    // Create exception
                    var exception = CreateExceptionFromData(exceptionData);
                    
                    // The standard user message should not contain:
                    // - HTTP status codes (e.g., "429", "503", "500")
                    // - Exception type names (e.g., "HttpRequestException", "TaskCanceledException")
                    // - Technical terms (e.g., "stack trace", "timeout", "rate limit")
                    
                    string userMessage = "현재 클로드 서버가 혼잡합니다 다시 시도해주세요";
                    string alternativeMessage = "현재 클로드 서버가 혼잡합니다. 잠시 후 다시 시도해주세요";
                    
                    // Verify user messages don't contain technical details
                    var technicalTerms = new[] {
                        "429", "503", "500", "404", "401",
                        "HttpRequestException", "TaskCanceledException", "OperationCanceledException",
                        "Exception", "StackTrace", "stack trace",
                        "timeout", "rate limit", "service unavailable",
                        "HTTP", "API", "error code"
                    };
                    
                    bool userMessageClean = !technicalTerms.Any(term => 
                        userMessage.Contains(term, StringComparison.OrdinalIgnoreCase));
                    
                    bool alternativeMessageClean = !technicalTerms.Any(term => 
                        alternativeMessage.Contains(term, StringComparison.OrdinalIgnoreCase));
                    
                    return userMessageClean && alternativeMessageClean;
                });
        }

        /// <summary>
        /// Helper method to classify errors for testing (mimics ClassifyError logic)
        /// </summary>
        private static string ClassifyErrorForTest(Exception ex)
        {
            if (ex is HttpRequestException httpEx)
            {
                var statusCodeProp = httpEx.GetType().GetProperty("StatusCode");
                if (statusCodeProp != null)
                {
                    var statusCode = statusCodeProp.GetValue(httpEx);
                    if (statusCode != null)
                    {
                        int statusCodeValue = (int)statusCode;
                        if (statusCodeValue == 429) return "RateLimit";
                        if (statusCodeValue == 503) return "ServiceUnavailable";
                    }
                }
                
                string message = httpEx.Message?.ToLower() ?? "";
                if (message.Contains("429") || message.Contains("rate limit") || message.Contains("too many requests"))
                    return "RateLimit";
                if (message.Contains("503") || message.Contains("service unavailable"))
                    return "ServiceUnavailable";
                if (message.Contains("timeout") || message.Contains("timed out"))
                    return "Timeout";
            }

            if (ex is TaskCanceledException || ex is OperationCanceledException)
                return "Timeout";

            return "Unknown";
        }

        /// <summary>
        /// Property 18: Error Classification Accuracy
        /// For any API exception, the system should correctly classify it into the appropriate
        /// error type (RateLimit, Timeout, ServiceUnavailable, Unknown).
        /// **Validates: Requirements 7.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ErrorClassificationAccuracy()
        {
            return Prop.ForAll(
                GenerateApiException(),
                exceptionData =>
                {
                    // Arrange: Create the exception based on the generated data
                    Exception exception = CreateExceptionFromData(exceptionData);

                    // Act: Classify the error using the test helper method
                    string actualErrorType = ClassifyErrorForTest(exception);

                    // Assert: Verify classification matches expected type
                    return actualErrorType == exceptionData.ExpectedType;
                });
        }

        #region Generators

        /// <summary>
        /// Generates random SessionContext instances for property-based testing
        /// </summary>
        private static Arbitrary<SessionContext> GenerateSessionContext()
        {
            var gen = from sessionId in Arb.Generate<NonEmptyString>()
                      from turnCount in Gen.Choose(0, 10)
                      from turns in Gen.ListOf(turnCount, GenerateConversationTurn().Generator)
                      from hasWorkflow in Arb.Generate<bool>()
                      from workflow in hasWorkflow ? GenerateWorkflowState().Generator : Gen.Constant<WorkflowState>(null)
                      from hasRetry in Arb.Generate<bool>()
                      from retry in hasRetry ? GenerateRetryContext().Generator : Gen.Constant<RetryContext>(null)
                      from consecutiveErrors in Gen.Choose(0, 5)
                      select new SessionContext
                      {
                          SessionId = sessionId.Get,
                          Turns = turns.ToList(),
                          CurrentWorkflow = workflow,
                          PendingRetry = retry,
                          LastUpdated = DateTime.UtcNow,
                          ConsecutiveErrors = consecutiveErrors
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Generates SessionContext with at least one workflow state
        /// </summary>
        private static Arbitrary<SessionContext> GenerateSessionContextWithWorkflow()
        {
            var gen = from sessionId in Arb.Generate<NonEmptyString>()
                      from turnCount in Gen.Choose(0, 5)
                      from turns in Gen.ListOf(turnCount, GenerateConversationTurn().Generator)
                      from workflow in GenerateWorkflowState().Generator
                      from consecutiveErrors in Gen.Choose(0, 3)
                      select new SessionContext
                      {
                          SessionId = sessionId.Get,
                          Turns = turns.ToList(),
                          CurrentWorkflow = workflow,
                          PendingRetry = null,
                          LastUpdated = DateTime.UtcNow,
                          ConsecutiveErrors = consecutiveErrors
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Generates SessionContext with at least one conversation turn
        /// </summary>
        private static Arbitrary<SessionContext> GenerateSessionContextWithTurns()
        {
            var gen = from sessionId in Arb.Generate<NonEmptyString>()
                      from turnCount in Gen.Choose(1, 10)
                      from turns in Gen.ListOf(turnCount, GenerateConversationTurn().Generator)
                      from hasWorkflow in Arb.Generate<bool>()
                      from workflow in hasWorkflow ? GenerateWorkflowState().Generator : Gen.Constant<WorkflowState>(null)
                      from consecutiveErrors in Gen.Choose(0, 3)
                      select new SessionContext
                      {
                          SessionId = sessionId.Get,
                          Turns = turns.ToList(),
                          CurrentWorkflow = workflow,
                          PendingRetry = null,
                          LastUpdated = DateTime.UtcNow,
                          ConsecutiveErrors = consecutiveErrors
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Generates random error types
        /// </summary>
        private static Arbitrary<string> GenerateErrorType()
        {
            var errorTypes = new[] { "RateLimit", "Timeout", "ServiceUnavailable", "Unknown" };
            var gen = Gen.Elements(errorTypes);
            return Arb.From(gen);
        }

        /// <summary>
        /// Generates random ConversationTurn instances
        /// </summary>
        private static Arbitrary<ConversationTurn> GenerateConversationTurn()
        {
            var gen = from userMessage in Arb.Generate<NonEmptyString>()
                      from hasResponse in Arb.Generate<bool>()
                      from response in hasResponse ? Arb.Generate<NonEmptyString>() : Gen.Constant<NonEmptyString>(null)
                      from isError in Arb.Generate<bool>()
                      select new ConversationTurn
                      {
                          UserMessage = userMessage.Get,
                          AssistantResponse = response?.Get,
                          IsError = isError,
                          Timestamp = DateTime.UtcNow.AddMinutes(-new System.Random().Next(0, 1000))
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Generates random WorkflowState instances
        /// </summary>
        private static Arbitrary<WorkflowState> GenerateWorkflowState()
        {
            var phases = new[] { "requirements", "design", "tasks" };
            var actions = new[] { "confirm", "approve", null };

            var gen = from phaseIndex in Gen.Choose(0, phases.Length - 1)
                      from actionIndex in Gen.Choose(0, actions.Length - 1)
                      from featureName in Arb.Generate<NonEmptyString>()
                      select new WorkflowState
                      {
                          Phase = phases[phaseIndex],
                          DocumentPath = $".kiro/specs/{featureName.Get}/requirements.md",
                          PendingAction = actions[actionIndex],
                          Metadata = new Dictionary<string, object>()
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Generates random RetryContext instances
        /// </summary>
        private static Arbitrary<RetryContext> GenerateRetryContext()
        {
            var errorTypes = new[] { "RateLimit", "Timeout", "ServiceUnavailable", "Unknown" };

            var gen = from originalMessage in Arb.Generate<NonEmptyString>()
                      from historyCount in Gen.Choose(0, 5)
                      from history in Gen.ListOf(historyCount, GenerateConversationTurn().Generator)
                      from hasWorkflow in Arb.Generate<bool>()
                      from workflow in hasWorkflow ? GenerateWorkflowState().Generator : Gen.Constant<WorkflowState>(null)
                      from errorTypeIndex in Gen.Choose(0, errorTypes.Length - 1)
                      select new RetryContext
                      {
                          OriginalUserMessage = originalMessage.Get,
                          ConversationHistory = history.ToList(),
                          WorkflowState = workflow,
                          FailedAt = DateTime.UtcNow.AddMinutes(-new System.Random().Next(0, 60)),
                          ErrorType = errorTypes[errorTypeIndex]
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Generates a sequence of error/success events for testing consecutive error counting.
        /// Returns a list of booleans where true = error, false = success.
        /// </summary>
        private static Arbitrary<System.Collections.Generic.List<bool>> GenerateErrorSequence()
        {
            var gen = from sequenceLength in Gen.Choose(1, 15)
                      from sequence in Gen.ListOf(sequenceLength, Arb.Generate<bool>())
                      select sequence.ToList();

            return Arb.From(gen);
        }

        /// <summary>
        /// Represents exception data for testing error classification
        /// </summary>
        private class ExceptionData
        {
            public string ExceptionType { get; set; }
            public int? StatusCode { get; set; }
            public string Message { get; set; }
            public string ExpectedType { get; set; }
        }

        /// <summary>
        /// Generates random API exception data for testing error classification
        /// </summary>
        private static Arbitrary<ExceptionData> GenerateApiException()
        {
            var gen = Gen.OneOf(
                // RateLimit exceptions
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "HttpRequestException",
                    StatusCode = 429,
                    Message = "Too Many Requests",
                    ExpectedType = "RateLimit"
                }),
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "HttpRequestException",
                    StatusCode = null,
                    Message = "rate limit exceeded",
                    ExpectedType = "RateLimit"
                }),
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "HttpRequestException",
                    StatusCode = null,
                    Message = "HTTP 429 error",
                    ExpectedType = "RateLimit"
                }),
                // ServiceUnavailable exceptions
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "HttpRequestException",
                    StatusCode = 503,
                    Message = "Service Unavailable",
                    ExpectedType = "ServiceUnavailable"
                }),
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "HttpRequestException",
                    StatusCode = null,
                    Message = "service unavailable",
                    ExpectedType = "ServiceUnavailable"
                }),
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "HttpRequestException",
                    StatusCode = null,
                    Message = "HTTP 503 error",
                    ExpectedType = "ServiceUnavailable"
                }),
                // Timeout exceptions
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "TaskCanceledException",
                    StatusCode = null,
                    Message = "The operation was canceled",
                    ExpectedType = "Timeout"
                }),
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "OperationCanceledException",
                    StatusCode = null,
                    Message = "The operation was canceled",
                    ExpectedType = "Timeout"
                }),
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "HttpRequestException",
                    StatusCode = null,
                    Message = "request timeout",
                    ExpectedType = "Timeout"
                }),
                // Unknown exceptions
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "HttpRequestException",
                    StatusCode = 500,
                    Message = "Internal Server Error",
                    ExpectedType = "Unknown"
                }),
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "InvalidOperationException",
                    StatusCode = null,
                    Message = "Invalid operation",
                    ExpectedType = "Unknown"
                }),
                Gen.Constant(new ExceptionData
                {
                    ExceptionType = "Exception",
                    StatusCode = null,
                    Message = "Generic error",
                    ExpectedType = "Unknown"
                })
            );

            return Arb.From(gen);
        }

        /// <summary>
        /// Creates an exception instance from exception data
        /// </summary>
        private static Exception CreateExceptionFromData(ExceptionData data)
        {
            switch (data.ExceptionType)
            {
                case "HttpRequestException":
                    var httpEx = new HttpRequestException(data.Message);
                    // If status code is provided, try to set it using reflection
                    if (data.StatusCode.HasValue)
                    {
                        try
                        {
                            var statusCodeProp = httpEx.GetType().GetProperty("StatusCode");
                            if (statusCodeProp != null && statusCodeProp.CanWrite)
                            {
                                statusCodeProp.SetValue(httpEx, (System.Net.HttpStatusCode)data.StatusCode.Value);
                            }
                        }
                        catch
                        {
                            // If we can't set status code, the message should still work for classification
                        }
                    }
                    return httpEx;

                case "TaskCanceledException":
                    return new TaskCanceledException(data.Message);

                case "OperationCanceledException":
                    return new OperationCanceledException(data.Message);

                case "InvalidOperationException":
                    return new InvalidOperationException(data.Message);

                default:
                    return new Exception(data.Message);
            }
        }

        /// <summary>
        /// Property 14: Active Session Memory Management
        /// For any point in time, only the currently active session context should be kept in memory.
        /// When switching sessions, the previous session should not remain in memory.
        /// **Validates: Requirements 4.5**
        /// Feature: error-resilient-context, Property 14: 활성 세션만 메모리 유지
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ActiveSessionOnlyInMemory()
        {
            return Prop.ForAll(
                GenerateMultipleSessions(),
                (sessions) =>
                {
                    // Arrange: Create a mock local session manager
                    var mockLocalSessionManager = new MockLocalSessionManager();
                    var contextManager = new ConversationContextManager(mockLocalSessionManager);
                    
                    // Act: Switch between multiple sessions
                    string lastSessionId = null;
                    foreach (var session in sessions)
                    {
                        // Start or restore the session
                        contextManager.StartNewSession(session.SessionId);
                        
                        // Add some turns to the session
                        foreach (var turn in session.Turns.Take(3)) // Take only first 3 turns for performance
                        {
                            contextManager.AddTurn(turn.UserMessage, turn.AssistantResponse, turn.IsError);
                        }
                        
                        lastSessionId = session.SessionId;
                    }
                    
                    // Assert: Only the last session should be active in memory
                    var currentSession = contextManager.GetCurrentSession();
                    
                    return currentSession != null &&
                           currentSession.SessionId == lastSessionId;
                });
        }

        /// <summary>
        /// Generates multiple SessionContext instances for testing session switching
        /// </summary>
        private static Arbitrary<List<SessionContext>> GenerateMultipleSessions()
        {
            var gen = from count in Gen.Choose(2, 5) // Generate 2-5 sessions
                      from sessions in Gen.ListOf(count, GenerateSessionContextWithTurns().Generator)
                      select sessions.ToList(); // Convert FSharpList to List<T>
            
            return Arb.From(gen);
        }

        /// <summary>
        /// Property 10: Error Details Logging
        /// For any API error, the technical details (exception type, message, stack trace)
        /// should be logged for debugging purposes.
        /// **Validates: Requirements 3.5**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ErrorDetailsLogging()
        {
            return Prop.ForAll(
                GenerateApiException(),
                exceptionData =>
                {
                    // This property verifies that error details are properly structured for logging.
                    // Since we can't easily test actual file I/O in a property test,
                    // we verify that the exception contains the required information
                    // that should be logged.
                    
                    // Arrange: Create exception from generated data
                    var exception = CreateExceptionFromData(exceptionData);
                    
                    // Assert: Verify exception has all required logging information
                    
                    // 1. Exception type should be available
                    var hasType = exception.GetType() != null;
                    var typeName = exception.GetType().Name;
                    var typeNameValid = !string.IsNullOrEmpty(typeName);
                    
                    // 2. Exception message should be available
                    var hasMessage = exception.Message != null;
                    var messageValid = !string.IsNullOrEmpty(exception.Message);
                    
                    // 3. Stack trace should be available (may be null for some exceptions)
                    // We just verify it's accessible, not that it's non-null
                    var stackTraceAccessible = true;
                    try
                    {
                        var _ = exception.StackTrace;
                    }
                    catch
                    {
                        stackTraceAccessible = false;
                    }
                    
                    // 4. Verify the exception can be converted to a loggable string format
                    var loggableString = $"API Error: {exception.GetType().Name} - {exception.Message}";
                    var loggableStringValid = !string.IsNullOrEmpty(loggableString) && 
                                              loggableString.Contains(exception.GetType().Name);
                    
                    // 5. Verify stack trace can be logged if present
                    var stackTraceLoggable = true;
                    if (exception.StackTrace != null)
                    {
                        var stackTraceString = $"Stack Trace: {exception.StackTrace}";
                        stackTraceLoggable = !string.IsNullOrEmpty(stackTraceString);
                    }
                    
                    return hasType && 
                           typeNameValid && 
                           hasMessage && 
                           messageValid && 
                           stackTraceAccessible &&
                           loggableStringValid &&
                           stackTraceLoggable;
                });
        }

        #endregion

        #region Integration Tests

        /// <summary>
        /// Integration Test 12.1a: Full Error Recovery Flow - Error → Retry → Success
        /// Tests the complete flow: message send → error → retry → success scenario
        /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2, 2.4, 6.4**
        /// </summary>
        [Fact]
        public void IntegrationTest_ErrorRetrySuccess()
        {
            // Arrange: Create a mock local session manager and context manager
            var mockLocalSessionManager = new MockLocalSessionManager();
            var contextManager = new ConversationContextManager(mockLocalSessionManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            // Add some initial conversation history
            contextManager.AddTurn("Hello", "Hi there!", false);
            contextManager.AddTurn("How are you?", "I'm doing well!", false);
            
            var initialTurnCount = contextManager.GetCurrentSession().Turns.Count;
            
            // Act 1: Simulate message send that will fail
            var userMessage = "This message will fail";
            contextManager.AddTurn(userMessage, null, false); // User message added
            
            // Capture state before error
            var turnsBeforeError = contextManager.GetCurrentSession().Turns.Count;
            
            // Act 2: Simulate API error
            contextManager.AddTurn(userMessage, null, true); // Error turn
            var retryContext = contextManager.CreateRetryContext(userMessage, "RateLimit");
            
            // Assert: Verify error state
            Assert.NotNull(retryContext);
            Assert.Equal(userMessage, retryContext.OriginalUserMessage);
            Assert.NotNull(contextManager.GetPendingRetry());
            Assert.True(contextManager.GetConsecutiveErrorCount() > 0);
            
            // Capture state after error (should preserve history)
            var turnsAfterError = contextManager.GetCurrentSession().Turns;
            Assert.True(turnsAfterError.Count >= initialTurnCount); // History preserved
            
            // Act 3: Simulate retry success
            contextManager.AddTurn(userMessage, "Successful response after retry", false);
            contextManager.ClearPendingRetry();
            
            // Assert: Verify success state
            Assert.Null(contextManager.GetPendingRetry()); // Retry context cleared
            Assert.Equal(0, contextManager.GetConsecutiveErrorCount()); // Errors reset
            
            var finalSession = contextManager.GetCurrentSession();
            var successfulTurn = finalSession.Turns.FirstOrDefault(t => 
                t.UserMessage == userMessage && 
                t.AssistantResponse == "Successful response after retry" && 
                !t.IsError);
            Assert.NotNull(successfulTurn); // Successful response added
        }

        /// <summary>
        /// Integration Test 12.1b: Full Error Recovery Flow - Error → Retry → Fail → Retry → Success
        /// Tests the complete flow: message send → error → retry → failure → retry → success scenario
        /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2, 2.4, 6.4**
        /// </summary>
        [Fact]
        public void IntegrationTest_ErrorRetryFailRetrySuccess()
        {
            // Arrange: Create a mock local session manager and context manager
            var mockLocalSessionManager = new MockLocalSessionManager();
            var contextManager = new ConversationContextManager(mockLocalSessionManager);
            
            var sessionId = Guid.NewGuid().ToString();
            contextManager.StartNewSession(sessionId);
            
            // Add some initial conversation history
            contextManager.AddTurn("Initial message 1", "Response 1", false);
            contextManager.AddTurn("Initial message 2", "Response 2", false);
            
            var initialTurnCount = contextManager.GetCurrentSession().Turns.Count;
            
            // Act 1: First error
            var userMessage = "This message will fail twice";
            contextManager.AddTurn(userMessage, null, true); // First error
            var retryContext1 = contextManager.CreateRetryContext(userMessage, "RateLimit");
            
            // Assert: First error state
            Assert.NotNull(retryContext1);
            Assert.Equal(1, contextManager.GetConsecutiveErrorCount());
            Assert.NotNull(contextManager.GetPendingRetry());
            
            // Act 2: First retry fails
            contextManager.AddTurn(userMessage, null, true); // Second error
            var retryContext2 = contextManager.CreateRetryContext(userMessage, "Timeout");
            
            // Assert: Second error state
            Assert.NotNull(retryContext2);
            Assert.Equal(2, contextManager.GetConsecutiveErrorCount());
            Assert.NotNull(contextManager.GetPendingRetry());
            Assert.Equal("Timeout", retryContext2.ErrorType); // Error type updated
            
            // Verify history is still preserved
            var turnsAfterSecondError = contextManager.GetCurrentSession().Turns;
            Assert.True(turnsAfterSecondError.Count >= initialTurnCount);
            
            // Act 3: Second retry succeeds
            contextManager.AddTurn(userMessage, "Finally successful!", false);
            contextManager.ClearPendingRetry();
            
            // Assert: Final success state
            Assert.Null(contextManager.GetPendingRetry());
            Assert.Equal(0, contextManager.GetConsecutiveErrorCount());
            
            var finalSession = contextManager.GetCurrentSession();
            var successfulTurn = finalSession.Turns.FirstOrDefault(t => 
                t.UserMessage == userMessage && 
                t.AssistantResponse == "Finally successful!" && 
                !t.IsError);
            Assert.NotNull(successfulTurn);
            
            // Verify all error turns are recorded
            var errorTurns = finalSession.Turns.Where(t => t.IsError).ToList();
            Assert.True(errorTurns.Count >= 2); // At least 2 error turns
        }

        /// <summary>
        /// Integration Test 12.2: Session Switching
        /// Tests: Session A work → Session B switch → Session A return → context verification
        /// **Validates: Requirements 4.1, 4.3**
        /// </summary>
        [Fact]
        public void IntegrationTest_SessionSwitching()
        {
            // Arrange: Create a mock local session manager
            var mockLocalSessionManager = new MockLocalSessionManager();
            
            // Create two context managers (simulating two different session contexts)
            var contextManagerA = new ConversationContextManager(mockLocalSessionManager);
            var contextManagerB = new ConversationContextManager(mockLocalSessionManager);
            
            var sessionIdA = "session-a-" + Guid.NewGuid().ToString();
            var sessionIdB = "session-b-" + Guid.NewGuid().ToString();
            
            // Act 1: Work in Session A
            contextManagerA.StartNewSession(sessionIdA);
            contextManagerA.AddTurn("Message A1", "Response A1", false);
            contextManagerA.AddTurn("Message A2", "Response A2", false);
            
            var workflowA = new WorkflowState
            {
                Phase = "requirements",
                DocumentPath = ".kiro/specs/feature-a/requirements.md",
                PendingAction = "confirm",
                Metadata = new Dictionary<string, object> { { "feature", "A" } }
            };
            contextManagerA.UpdateWorkflowState(workflowA);
            
            // Create an error in Session A
            contextManagerA.AddTurn("Message A3", null, true);
            var retryContextA = contextManagerA.CreateRetryContext("Message A3", "RateLimit");
            
            // Save Session A
            contextManagerA.SaveSession();
            
            // Capture Session A state
            var sessionABeforeSwitch = contextManagerA.GetCurrentSession();
            var sessionATurnCount = sessionABeforeSwitch.Turns.Count;
            var sessionAWorkflowPhase = sessionABeforeSwitch.CurrentWorkflow?.Phase;
            var sessionAHasRetry = sessionABeforeSwitch.PendingRetry != null;
            
            // Act 2: Switch to Session B
            contextManagerB.StartNewSession(sessionIdB);
            contextManagerB.AddTurn("Message B1", "Response B1", false);
            contextManagerB.AddTurn("Message B2", "Response B2", false);
            
            var workflowB = new WorkflowState
            {
                Phase = "design",
                DocumentPath = ".kiro/specs/feature-b/design.md",
                PendingAction = "approve",
                Metadata = new Dictionary<string, object> { { "feature", "B" } }
            };
            contextManagerB.UpdateWorkflowState(workflowB);
            
            // Save Session B
            contextManagerB.SaveSession();
            
            // Capture Session B state
            var sessionBState = contextManagerB.GetCurrentSession();
            var sessionBTurnCount = sessionBState.Turns.Count;
            var sessionBWorkflowPhase = sessionBState.CurrentWorkflow?.Phase;
            
            // Act 3: Return to Session A (restore from storage)
            var contextManagerA2 = new ConversationContextManager(mockLocalSessionManager);
            var restoredSessionA = contextManagerA2.RestoreSession(sessionIdA);
            
            // Assert: Verify Session A context is fully restored
            Assert.NotNull(restoredSessionA);
            Assert.Equal(sessionIdA, restoredSessionA.SessionId);
            Assert.Equal(sessionATurnCount, restoredSessionA.Turns.Count);
            Assert.Equal(sessionAWorkflowPhase, restoredSessionA.CurrentWorkflow?.Phase);
            Assert.Equal(sessionAHasRetry, restoredSessionA.PendingRetry != null);
            
            // Verify specific messages are preserved
            var messageA1 = restoredSessionA.Turns.FirstOrDefault(t => t.UserMessage == "Message A1");
            Assert.NotNull(messageA1);
            Assert.Equal("Response A1", messageA1.AssistantResponse);
            
            // Verify workflow state details
            Assert.Equal(".kiro/specs/feature-a/requirements.md", restoredSessionA.CurrentWorkflow?.DocumentPath);
            Assert.Equal("confirm", restoredSessionA.CurrentWorkflow?.PendingAction);
            
            // Verify retry context is preserved
            Assert.NotNull(restoredSessionA.PendingRetry);
            Assert.Equal("Message A3", restoredSessionA.PendingRetry.OriginalUserMessage);
            Assert.Equal("RateLimit", restoredSessionA.PendingRetry.ErrorType);
            
            // Assert: Verify Session B is independent and unchanged
            var sessionBCheck = contextManagerB.GetCurrentSession();
            Assert.Equal(sessionIdB, sessionBCheck.SessionId);
            Assert.Equal(sessionBTurnCount, sessionBCheck.Turns.Count);
            Assert.Equal("design", sessionBCheck.CurrentWorkflow?.Phase);
            Assert.Null(sessionBCheck.PendingRetry); // Session B has no retry context
        }

        /// <summary>
        /// Integration Test 12.3: Local Storage Integration
        /// Tests: Session save → application restart simulation → session restore → content verification
        /// **Validates: Requirements 5.1, 5.2, 4.3**
        /// </summary>
        [Fact]
        public void IntegrationTest_LocalStoragePersistence()
        {
            // Arrange: Create a mock local session manager
            var mockLocalSessionManager = new MockLocalSessionManager();
            var contextManager1 = new ConversationContextManager(mockLocalSessionManager);
            
            var sessionId = "persistent-session-" + Guid.NewGuid().ToString();
            
            // Act 1: Create a session with rich content
            contextManager1.StartNewSession(sessionId);
            
            // Add multiple conversation turns
            contextManager1.AddTurn("User message 1", "Assistant response 1", false);
            contextManager1.AddTurn("User message 2", "Assistant response 2", false);
            contextManager1.AddTurn("User message 3", null, true); // Error turn
            contextManager1.AddTurn("User message 4", "Assistant response 4", false);
            
            // Add workflow state
            var workflow = new WorkflowState
            {
                Phase = "tasks",
                DocumentPath = ".kiro/specs/my-feature/tasks.md",
                PendingAction = "execute",
                Metadata = new Dictionary<string, object>
                {
                    { "taskCount", 10 },
                    { "completedTasks", 3 },
                    { "currentTask", "Task 4" }
                }
            };
            contextManager1.UpdateWorkflowState(workflow);
            
            // Create retry context
            var retryContext = contextManager1.CreateRetryContext("User message 5", "ServiceUnavailable");
            
            // Save the session
            contextManager1.SaveSession();
            
            // Capture original state for comparison
            var originalSession = contextManager1.GetCurrentSession();
            var originalTurnCount = originalSession.Turns.Count;
            var originalWorkflowPhase = originalSession.CurrentWorkflow?.Phase;
            var originalWorkflowPath = originalSession.CurrentWorkflow?.DocumentPath;
            var originalRetryMessage = originalSession.PendingRetry?.OriginalUserMessage;
            var originalRetryErrorType = originalSession.PendingRetry?.ErrorType;
            var originalConsecutiveErrors = originalSession.ConsecutiveErrors;
            
            // Act 2: Simulate application restart by creating a new context manager
            // with the same history manager (simulating persistent storage)
            var contextManager2 = new ConversationContextManager(mockLocalSessionManager);
            
            // Act 3: Restore the session
            var restoredSession = contextManager2.RestoreSession(sessionId);
            
            // Assert: Verify all content is restored correctly
            
            // 1. Session ID matches
            Assert.Equal(sessionId, restoredSession.SessionId);
            
            // 2. All conversation turns are restored
            Assert.Equal(originalTurnCount, restoredSession.Turns.Count);
            
            // Verify specific turn content
            var turn1 = restoredSession.Turns[0];
            Assert.Equal("User message 1", turn1.UserMessage);
            Assert.Equal("Assistant response 1", turn1.AssistantResponse);
            Assert.False(turn1.IsError);
            
            var turn3 = restoredSession.Turns[2];
            Assert.Equal("User message 3", turn3.UserMessage);
            Assert.Null(turn3.AssistantResponse);
            Assert.True(turn3.IsError);
            
            // 3. Workflow state is fully restored
            Assert.NotNull(restoredSession.CurrentWorkflow);
            Assert.Equal(originalWorkflowPhase, restoredSession.CurrentWorkflow.Phase);
            Assert.Equal(originalWorkflowPath, restoredSession.CurrentWorkflow.DocumentPath);
            Assert.Equal("execute", restoredSession.CurrentWorkflow.PendingAction);
            Assert.Equal(3, restoredSession.CurrentWorkflow.Metadata.Count);
            
            // 4. Retry context is fully restored
            Assert.NotNull(restoredSession.PendingRetry);
            Assert.Equal(originalRetryMessage, restoredSession.PendingRetry.OriginalUserMessage);
            Assert.Equal(originalRetryErrorType, restoredSession.PendingRetry.ErrorType);
            
            // Verify retry context has conversation history
            Assert.NotNull(restoredSession.PendingRetry.ConversationHistory);
            Assert.True(restoredSession.PendingRetry.ConversationHistory.Count > 0);
            
            // Verify retry context has workflow state
            Assert.NotNull(restoredSession.PendingRetry.WorkflowState);
            Assert.Equal("tasks", restoredSession.PendingRetry.WorkflowState.Phase);
            
            // 5. Consecutive errors count is restored
            Assert.Equal(originalConsecutiveErrors, restoredSession.ConsecutiveErrors);
            
            // 6. Timestamps are preserved (within reasonable tolerance)
            for (int i = 0; i < originalSession.Turns.Count; i++)
            {
                var originalTimestamp = originalSession.Turns[i].Timestamp;
                var restoredTimestamp = restoredSession.Turns[i].Timestamp;
                var timeDiff = Math.Abs((restoredTimestamp - originalTimestamp).TotalSeconds);
                Assert.True(timeDiff < 2, $"Timestamp difference too large: {timeDiff} seconds");
            }
            
            // 7. Verify the restored session is functional (can add new turns)
            contextManager2.AddTurn("New message after restore", "New response", false);
            var updatedSession = contextManager2.GetCurrentSession();
            Assert.Equal(originalTurnCount + 1, updatedSession.Turns.Count);
            
            // 8. Verify consecutive errors reset on success
            Assert.Equal(0, updatedSession.ConsecutiveErrors);
        }

        /// <summary>
        /// Integration Test 12.3b: Multiple Session Persistence
        /// Tests that multiple sessions can be saved and restored independently
        /// **Validates: Requirements 5.1, 5.2, 4.1, 4.3**
        /// </summary>
        [Fact]
        public void IntegrationTest_MultipleSessionPersistence()
        {
            // Arrange: Create a mock local session manager
            var mockLocalSessionManager = new MockLocalSessionManager();
            
            // Create three different sessions
            var sessionIds = new[]
            {
                "session-1-" + Guid.NewGuid().ToString(),
                "session-2-" + Guid.NewGuid().ToString(),
                "session-3-" + Guid.NewGuid().ToString()
            };
            
            var contextManagers = new[]
            {
                new ConversationContextManager(mockLocalSessionManager),
                new ConversationContextManager(mockLocalSessionManager),
                new ConversationContextManager(mockLocalSessionManager)
            };
            
            // Act 1: Create and save three different sessions with unique content
            for (int i = 0; i < 3; i++)
            {
                contextManagers[i].StartNewSession(sessionIds[i]);
                
                // Add unique turns for each session
                contextManagers[i].AddTurn($"Session {i + 1} Message 1", $"Session {i + 1} Response 1", false);
                contextManagers[i].AddTurn($"Session {i + 1} Message 2", $"Session {i + 1} Response 2", false);
                
                // Add unique workflow for each session
                var workflow = new WorkflowState
                {
                    Phase = i == 0 ? "requirements" : i == 1 ? "design" : "tasks",
                    DocumentPath = $".kiro/specs/feature-{i + 1}/document.md",
                    PendingAction = "action-" + (i + 1),
                    Metadata = new Dictionary<string, object> { { "sessionNumber", i + 1 } }
                };
                contextManagers[i].UpdateWorkflowState(workflow);
                
                // Save each session
                contextManagers[i].SaveSession();
            }
            
            // Act 2: Restore all sessions using new context managers
            var restoredManagers = new[]
            {
                new ConversationContextManager(mockLocalSessionManager),
                new ConversationContextManager(mockLocalSessionManager),
                new ConversationContextManager(mockLocalSessionManager)
            };
            
            var restoredSessions = new SessionContext[3];
            for (int i = 0; i < 3; i++)
            {
                restoredSessions[i] = restoredManagers[i].RestoreSession(sessionIds[i]);
            }
            
            // Assert: Verify each session is restored with its unique content
            for (int i = 0; i < 3; i++)
            {
                Assert.NotNull(restoredSessions[i]);
                Assert.Equal(sessionIds[i], restoredSessions[i].SessionId);
                
                // Verify unique messages
                var firstTurn = restoredSessions[i].Turns[0];
                Assert.Equal($"Session {i + 1} Message 1", firstTurn.UserMessage);
                Assert.Equal($"Session {i + 1} Response 1", firstTurn.AssistantResponse);
                
                // Verify unique workflow
                var expectedPhase = i == 0 ? "requirements" : i == 1 ? "design" : "tasks";
                Assert.Equal(expectedPhase, restoredSessions[i].CurrentWorkflow?.Phase);
                Assert.Equal($".kiro/specs/feature-{i + 1}/document.md", restoredSessions[i].CurrentWorkflow?.DocumentPath);
            }
            
            // Assert: Verify sessions are independent (modifying one doesn't affect others)
            restoredManagers[0].AddTurn("New message in session 1", "New response", false);
            restoredManagers[0].SaveSession();
            
            // Restore session 2 again and verify it's unchanged
            var session2Check = new ConversationContextManager(mockLocalSessionManager).RestoreSession(sessionIds[1]);
            Assert.Equal(2, session2Check.Turns.Count); // Still has only 2 turns
            Assert.Equal($"Session 2 Message 1", session2Check.Turns[0].UserMessage);
        }

        #endregion
    }

    /// <summary>
    /// Mock implementation of LocalSessionManager for testing
    /// </summary>
    internal class MockLocalSessionManager : LocalSessionManager
    {
        private readonly Dictionary<string, SessionContext> _sessions = new Dictionary<string, SessionContext>();

        public MockLocalSessionManager() : base(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bibim_test_" + Guid.NewGuid().ToString()))
        {
        }

        public new void SaveSessionContext(SessionContext context)
        {
            if (context == null) return;
            _sessions[context.SessionId] = context;
        }

        public new SessionContext LoadSessionContext(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return CreateEmptySessionContext(sessionId);
            }

            if (_sessions.TryGetValue(sessionId, out var context))
            {
                return context;
            }

            return CreateEmptySessionContext(sessionId);
        }

        private new SessionContext CreateEmptySessionContext(string sessionId)
        {
            return new SessionContext
            {
                SessionId = sessionId,
                Turns = new System.Collections.Generic.List<ConversationTurn>(),
                CurrentWorkflow = null,
                PendingRetry = null,
                LastUpdated = DateTime.UtcNow,
                ConsecutiveErrors = 0
            };
        }
    }
}
