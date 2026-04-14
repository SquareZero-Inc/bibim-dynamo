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
    /// A single clarifying question with selectable options.
    /// Not serialized — runtime UI only. Flat text is stored in ClarifyingQuestions for context/history.
    /// </summary>
    public class ClarifyingQuestion
    {
        public string Question { get; set; }
        public List<string> Options { get; set; }

        public ClarifyingQuestion()
        {
            Question = string.Empty;
            Options = new List<string>();
        }
    }

    /// <summary>
    /// Represents an input element for code specification.
    /// </summary>
    public class SpecInput
    {
#if NET48
        [JsonProperty("name")]
#else
        [JsonPropertyName("name")]
#endif
        public string Name { get; set; }

#if NET48
        [JsonProperty("type")]
#else
        [JsonPropertyName("type")]
#endif
        public string Type { get; set; }  // e.g., "Wall elements", "Parameter name"

#if NET48
        [JsonProperty("description")]
#else
        [JsonPropertyName("description")]
#endif
        public string Description { get; set; }

        public SpecInput()
        {
            Name = string.Empty;
            Type = string.Empty;
            Description = string.Empty;
        }
    }

    /// <summary>
    /// Represents the expected output for code specification.
    /// </summary>
    public class SpecOutput
    {
#if NET48
        [JsonProperty("type")]
#else
        [JsonPropertyName("type")]
#endif
        public string Type { get; set; }  // e.g., "Number", "List", "Modified elements"

#if NET48
        [JsonProperty("description")]
#else
        [JsonPropertyName("description")]
#endif
        public string Description { get; set; }

#if NET48
        [JsonProperty("unit")]
#else
        [JsonPropertyName("unit")]
#endif
        public string Unit { get; set; }  // e.g., "㎡", "count"

        public SpecOutput()
        {
            Type = string.Empty;
            Description = string.Empty;
            Unit = string.Empty;
        }
    }

    /// <summary>
    /// Represents a structured specification for code generation.
    /// This is the intermediate step between user request and code generation,
    /// allowing users to review and confirm the AI's understanding before code is generated.
    /// </summary>
    public class CodeSpecification
    {
        /// <summary>Unique identifier for the specification</summary>
#if NET48
        [JsonProperty("specId")]
#else
        [JsonPropertyName("specId")]
#endif
        public string SpecId { get; set; }

        /// <summary>Original user request that triggered this spec</summary>
#if NET48
        [JsonProperty("originalRequest")]
#else
        [JsonPropertyName("originalRequest")]
#endif
        public string OriginalRequest { get; set; }

        /// <summary>List of input elements the code will use</summary>
#if NET48
        [JsonProperty("inputs")]
#else
        [JsonPropertyName("inputs")]
#endif
        public List<SpecInput> Inputs { get; set; }

        /// <summary>Processing steps the code will perform</summary>
#if NET48
        [JsonProperty("processingSteps")]
#else
        [JsonPropertyName("processingSteps")]
#endif
        public List<string> ProcessingSteps { get; set; }

        /// <summary>Expected output description</summary>
#if NET48
        [JsonProperty("output")]
#else
        [JsonPropertyName("output")]
#endif
        public SpecOutput Output { get; set; }

        /// <summary>Clarifying questions for the user (if any)</summary>
#if NET48
        [JsonProperty("clarifyingQuestions")]
#else
        [JsonPropertyName("clarifyingQuestions")]
#endif
        public List<string> ClarifyingQuestions { get; set; }

        /// <summary>
        /// Structured form of clarifying questions with selectable options.
        /// Runtime-only (not serialized). Populated alongside ClarifyingQuestions.
        /// Used to render the interactive question form UI.
        /// </summary>
        [JsonIgnore]
        public List<ClarifyingQuestion> ClarifyingQuestionsStructured { get; set; }

        /// <summary>Revision number for iterative refinement</summary>
#if NET48
        [JsonProperty("revisionNumber")]
#else
        [JsonPropertyName("revisionNumber")]
#endif
        public int RevisionNumber { get; set; }

        /// <summary>Timestamp when spec was created</summary>
#if NET48
        [JsonProperty("createdAt")]
#else
        [JsonPropertyName("createdAt")]
#endif
        public DateTime CreatedAt { get; set; }

        /// <summary>Whether this spec has been confirmed by user</summary>
#if NET48
        [JsonProperty("isConfirmed")]
#else
        [JsonPropertyName("isConfirmed")]
#endif
        public bool IsConfirmed { get; set; }

        /// <summary>
        /// Indicates this is a general chat response, not a code specification.
        /// When true, ChatResponseText contains the response and other fields are empty.
        /// </summary>
        [JsonIgnore]
        public bool IsChatResponse { get; set; }

        /// <summary>
        /// The chat response text when IsChatResponse is true.
        /// </summary>
        [JsonIgnore]
        public string ChatResponseText { get; set; }

        /// <summary>
        /// Creates a new CodeSpecification with default values.
        /// </summary>
        public CodeSpecification()
        {
            SpecId = Guid.NewGuid().ToString();
            OriginalRequest = string.Empty;
            Inputs = new List<SpecInput>();
            ProcessingSteps = new List<string>();
            Output = new SpecOutput();
            ClarifyingQuestions = new List<string>();
            ClarifyingQuestionsStructured = new List<ClarifyingQuestion>();
            RevisionNumber = 1;
            CreatedAt = DateTime.UtcNow;
            IsConfirmed = false;
            IsChatResponse = false;
            ChatResponseText = string.Empty;
        }
    }
}
