using System;
using System.Collections.Generic;
#if NET48
using Newtonsoft.Json;
#else
using System.Text.Json.Serialization;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Represents a single chat message. Used as conversation history DTO for Claude API calls.
    /// </summary>
    public class ChatMessage
    {
        public string Text { get; set; }
        public bool IsUser { get; set; }
    }

    /// <summary>
    /// Represents a chat session containing multiple messages.
    /// Used for local JSON storage.
    /// </summary>
    public class ChatSession
    {
#if NET48
        [JsonProperty("sessionId")]
#else
        [JsonPropertyName("sessionId")]
#endif
        public string SessionId { get; set; }

#if NET48
        [JsonProperty("title")]
#else
        [JsonPropertyName("title")]
#endif
        public string Title { get; set; }

#if NET48
        [JsonProperty("revitVersion")]
#else
        [JsonPropertyName("revitVersion")]
#endif
        public string RevitVersion { get; set; }

#if NET48
        [JsonProperty("createdAt")]
#else
        [JsonPropertyName("createdAt")]
#endif
        public DateTime CreatedAt { get; set; }

#if NET48
        [JsonProperty("updatedAt")]
#else
        [JsonPropertyName("updatedAt")]
#endif
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// DEPRECATED: Use SingleMessages instead. Kept for backward compatibility with old sessions.
        /// </summary>
#if NET48
        [JsonProperty("messages")]
#else
        [JsonPropertyName("messages")]
#endif
        public List<MessagePair> Messages { get; set; }

        /// <summary>
        /// Individual messages in chronological order.
        /// New sessions use this instead of Messages (MessagePair).
        /// </summary>
#if NET48
        [JsonProperty("singleMessages")]
#else
        [JsonPropertyName("singleMessages")]
#endif
        public List<SingleMessage> SingleMessages { get; set; }

        /// <summary>
        /// Serialized SessionContext JSON for error-resilient context management.
        /// Contains conversation turns, workflow state, and retry context.
        /// </summary>
#if NET48
        [JsonProperty("contextData")]
#else
        [JsonPropertyName("contextData")]
#endif
        public string ContextData { get; set; }

        public ChatSession()
        {
            Messages = new List<MessagePair>();
            SingleMessages = new List<SingleMessage>();
        }

        /// <summary>
        /// Check if this session uses the new SingleMessages format
        /// </summary>
#if NET48
        [JsonIgnore]
#else
        [JsonIgnore]
#endif
        public bool UsesNewFormat => SingleMessages != null && SingleMessages.Count > 0;
    }

    /// <summary>
    /// Represents a user prompt and AI response pair within a session.
    /// DEPRECATED: Use SingleMessage for new code. Kept for backward compatibility.
    /// </summary>
    public class MessagePair
    {
#if NET48
        [JsonProperty("userPrompt")]
#else
        [JsonPropertyName("userPrompt")]
#endif
        public string UserPrompt { get; set; }

#if NET48
        [JsonProperty("aiResponse")]
#else
        [JsonPropertyName("aiResponse")]
#endif
        public string AiResponse { get; set; }

#if NET48
        [JsonProperty("pythonCode")]
#else
        [JsonPropertyName("pythonCode")]
#endif
        public string PythonCode { get; set; }

#if NET48
        [JsonProperty("sequenceOrder")]
#else
        [JsonPropertyName("sequenceOrder")]
#endif
        public int SequenceOrder { get; set; }

#if NET48
        [JsonProperty("createdAt")]
#else
        [JsonPropertyName("createdAt")]
#endif
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Serialized CodeSpecification JSON for spec-first code generation.
        /// Links the specification to its resulting code for traceability.
        /// </summary>
#if NET48
        [JsonProperty("specificationJson")]
#else
        [JsonPropertyName("specificationJson")]
#endif
        public string SpecificationJson { get; set; }

        /// <summary>
        /// Indicates whether the specification was confirmed by the user before code generation.
        /// Used for tracking the spec-first workflow confirmation status.
        /// </summary>
#if NET48
        [JsonProperty("wasSpecConfirmed")]
#else
        [JsonPropertyName("wasSpecConfirmed")]
#endif
        public bool WasSpecConfirmed { get; set; }
    }

    /// <summary>
    /// Represents a single message in a conversation (user or AI).
    /// Used for accurate history recording with proper ordering.
    /// </summary>
    public class SingleMessage
    {
#if NET48
        [JsonProperty("id")]
#else
        [JsonPropertyName("id")]
#endif
        public string Id { get; set; }

        /// <summary>
        /// Message sender: "user", "assistant", "system"
        /// </summary>
#if NET48
        [JsonProperty("role")]
#else
        [JsonPropertyName("role")]
#endif
        public string Role { get; set; }

        /// <summary>
        /// Message content type: "text", "question", "spec", "code", "guide", "analysis"
        /// </summary>
#if NET48
        [JsonProperty("contentType")]
#else
        [JsonPropertyName("contentType")]
#endif
        public string ContentType { get; set; }

        /// <summary>
        /// Main message content (text, question, spec JSON, etc.)
        /// </summary>
#if NET48
        [JsonProperty("content")]
#else
        [JsonPropertyName("content")]
#endif
        public string Content { get; set; }

        /// <summary>
        /// Python code if contentType is "code"
        /// </summary>
#if NET48
        [JsonProperty("pythonCode")]
#else
        [JsonPropertyName("pythonCode")]
#endif
        public string PythonCode { get; set; }

        /// <summary>
        /// Sequence order for proper message ordering
        /// </summary>
#if NET48
        [JsonProperty("sequenceOrder")]
#else
        [JsonPropertyName("sequenceOrder")]
#endif
        public int SequenceOrder { get; set; }

#if NET48
        [JsonProperty("createdAt")]
#else
        [JsonPropertyName("createdAt")]
#endif
        public DateTime CreatedAt { get; set; }

        public SingleMessage()
        {
            Id = Guid.NewGuid().ToString();
            CreatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Root container for storing multiple sessions in JSON file.
    /// </summary>
    public class SessionStorage
    {
#if NET48
        [JsonProperty("sessions")]
#else
        [JsonPropertyName("sessions")]
#endif
        public List<ChatSession> Sessions { get; set; }

        public SessionStorage()
        {
            Sessions = new List<ChatSession>();
        }
    }

    // ============================================================================
    // Error-Resilient Context Management Models
    // ============================================================================

    /// <summary>
    /// Represents a single conversation turn (user message + AI response).
    /// Used for error-resilient context management.
    /// </summary>
    public class ConversationTurn
    {
#if NET48
        [JsonProperty("userMessage")]
#else
        [JsonPropertyName("userMessage")]
#endif
        public string UserMessage { get; set; }

#if NET48
        [JsonProperty("assistantResponse")]
#else
        [JsonPropertyName("assistantResponse")]
#endif
        public string AssistantResponse { get; set; }

#if NET48
        [JsonProperty("isError")]
#else
        [JsonPropertyName("isError")]
#endif
        public bool IsError { get; set; }

#if NET48
        [JsonProperty("timestamp")]
#else
        [JsonPropertyName("timestamp")]
#endif
        public DateTime Timestamp { get; set; }

        public ConversationTurn()
        {
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Represents the workflow state during spec generation.
    /// Tracks the current phase and pending actions.
    /// </summary>
    public class WorkflowState
    {
        /// <summary>
        /// Current workflow phase: "requirements", "design", "tasks"
        /// </summary>
#if NET48
        [JsonProperty("phase")]
#else
        [JsonPropertyName("phase")]
#endif
        public string Phase { get; set; }

        /// <summary>
        /// Path to the current document being worked on
        /// </summary>
#if NET48
        [JsonProperty("documentPath")]
#else
        [JsonPropertyName("documentPath")]
#endif
        public string DocumentPath { get; set; }

        /// <summary>
        /// Pending action: "confirm", "approve", etc.
        /// </summary>
#if NET48
        [JsonProperty("pendingAction")]
#else
        [JsonPropertyName("pendingAction")]
#endif
        public string PendingAction { get; set; }

        /// <summary>
        /// Additional metadata for workflow state
        /// </summary>
#if NET48
        [JsonProperty("metadata")]
#else
        [JsonPropertyName("metadata")]
#endif
        public Dictionary<string, object> Metadata { get; set; }

        public WorkflowState()
        {
            Metadata = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Contains all information needed to retry a failed API request.
    /// Preserves the original context for seamless retry without re-input.
    /// </summary>
    public class RetryContext
    {
        /// <summary>
        /// The original user message that failed
        /// </summary>
#if NET48
        [JsonProperty("originalUserMessage")]
#else
        [JsonPropertyName("originalUserMessage")]
#endif
        public string OriginalUserMessage { get; set; }

        /// <summary>
        /// Complete conversation history at the time of failure
        /// </summary>
#if NET48
        [JsonProperty("conversationHistory")]
#else
        [JsonPropertyName("conversationHistory")]
#endif
        public List<ConversationTurn> ConversationHistory { get; set; }

        /// <summary>
        /// Workflow state at the time of failure (if in spec workflow)
        /// </summary>
#if NET48
        [JsonProperty("workflowState")]
#else
        [JsonPropertyName("workflowState")]
#endif
        public WorkflowState WorkflowState { get; set; }

        /// <summary>
        /// Timestamp when the error occurred
        /// </summary>
#if NET48
        [JsonProperty("failedAt")]
#else
        [JsonPropertyName("failedAt")]
#endif
        public DateTime FailedAt { get; set; }

        /// <summary>
        /// Type of error: "RateLimit", "Timeout", "ServiceUnavailable", "Unknown"
        /// </summary>
#if NET48
        [JsonProperty("errorType")]
#else
        [JsonPropertyName("errorType")]
#endif
        public string ErrorType { get; set; }

        public RetryContext()
        {
            ConversationHistory = new List<ConversationTurn>();
            FailedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Represents the complete context of a chat session.
    /// Includes all conversation turns, workflow state, and retry context.
    /// Persisted locally for session restoration.
    /// </summary>
    public class SessionContext
    {
        /// <summary>
        /// Unique identifier for the session
        /// </summary>
#if NET48
        [JsonProperty("sessionId")]
#else
        [JsonPropertyName("sessionId")]
#endif
        public string SessionId { get; set; }

        /// <summary>
        /// All conversation turns in this session
        /// </summary>
#if NET48
        [JsonProperty("turns")]
#else
        [JsonPropertyName("turns")]
#endif
        public List<ConversationTurn> Turns { get; set; }

        /// <summary>
        /// Current workflow state (null if not in spec workflow)
        /// </summary>
#if NET48
        [JsonProperty("currentWorkflow")]
#else
        [JsonPropertyName("currentWorkflow")]
#endif
        public WorkflowState CurrentWorkflow { get; set; }

        /// <summary>
        /// Pending retry context (null if no retry pending)
        /// </summary>
#if NET48
        [JsonProperty("pendingRetry")]
#else
        [JsonPropertyName("pendingRetry")]
#endif
        public RetryContext PendingRetry { get; set; }

        /// <summary>
        /// Last update timestamp
        /// </summary>
#if NET48
        [JsonProperty("lastUpdated")]
#else
        [JsonPropertyName("lastUpdated")]
#endif
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Number of consecutive errors in this session
        /// </summary>
#if NET48
        [JsonProperty("consecutiveErrors")]
#else
        [JsonPropertyName("consecutiveErrors")]
#endif
        public int ConsecutiveErrors { get; set; }

        public SessionContext()
        {
            Turns = new List<ConversationTurn>();
            LastUpdated = DateTime.UtcNow;
            ConsecutiveErrors = 0;
        }
    }
}
