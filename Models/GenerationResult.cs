using System;

namespace BIBIM_MVP
{
    internal enum GenerationResultType
    {
        /// <summary>Python code was generated successfully. PythonCode and GuideText are populated.</summary>
        Code,
        /// <summary>Clarifying question returned (direct mode). Message contains the question text.</summary>
        Question,
        /// <summary>Validation gate blocked injection. Message contains the user-facing error.</summary>
        ValidationBlock,
        /// <summary>General conversational response. Message contains the response text.</summary>
        Chat
    }

    /// <summary>
    /// Structured result returned by the generation pipeline.
    /// Replaces the raw "TYPE: CODE|..." string protocol at the consumer boundary.
    /// GeminiService still composes strings internally; Parse() is called at the output boundary.
    /// </summary>
    internal sealed class GenerationResult
    {
        private const string PrefixCode            = "TYPE: CODE|";
        private const string PrefixGuide           = "TYPE: GUIDE|";
        private const string PrefixQuestion        = "TYPE: QUESTION|";
        private const string PrefixValidationBlock = "TYPE: VALIDATION_BLOCK|";

        public GenerationResultType Type { get; private set; }

        /// <summary>Cleaned Python code. Populated when Type == Code.</summary>
        public string PythonCode { get; private set; }

        /// <summary>Execution guide text. Populated when Type == Code (may be empty).</summary>
        public string GuideText { get; private set; }

        /// <summary>Response text. Populated for Question, ValidationBlock, and Chat types.</summary>
        public string Message { get; private set; }

        /// <summary>
        /// Original raw response string, preserved for history/context storage.
        /// Use this when passing back to ConversationContextManager or AddAssistantMessageToHistory.
        /// </summary>
        public string RawResponse { get; private set; }

        public bool IsCode            => Type == GenerationResultType.Code;
        public bool IsValidationBlock => Type == GenerationResultType.ValidationBlock;
        public bool IsQuestion        => Type == GenerationResultType.Question;
        public bool IsChat            => Type == GenerationResultType.Chat;

        private GenerationResult() { }

        /// <summary>
        /// Parses a raw pipeline response string into a typed GenerationResult.
        /// This is the single source of truth for all TYPE: protocol parsing.
        /// </summary>
        public static GenerationResult Parse(string rawResponse)
        {
            var result = new GenerationResult
            {
                RawResponse = rawResponse ?? string.Empty
            };

            string trimmed = (rawResponse ?? string.Empty).Trim();

            if (trimmed.StartsWith(PrefixQuestion, StringComparison.Ordinal))
            {
                result.Type    = GenerationResultType.Question;
                result.Message = trimmed.Substring(PrefixQuestion.Length).Trim();
            }
            else if (trimmed.StartsWith(PrefixValidationBlock, StringComparison.Ordinal))
            {
                result.Type    = GenerationResultType.ValidationBlock;
                result.Message = trimmed.Substring(PrefixValidationBlock.Length).Trim();
            }
            else if (trimmed.IndexOf(PrefixCode, StringComparison.Ordinal) >= 0)
            {
                result.Type = GenerationResultType.Code;

                int codeStart  = trimmed.IndexOf(PrefixCode, StringComparison.Ordinal) + PrefixCode.Length;
                int guideStart = trimmed.IndexOf(PrefixGuide, StringComparison.Ordinal);

                if (guideStart > 0)
                {
                    result.PythonCode = trimmed.Substring(codeStart, guideStart - codeStart).Trim();
                    result.GuideText  = trimmed.Substring(guideStart + PrefixGuide.Length).Trim();
                }
                else
                {
                    result.PythonCode = trimmed.Substring(codeStart).Trim();
                    result.GuideText  = string.Empty;
                }
            }
            else
            {
                result.Type    = GenerationResultType.Chat;
                result.Message = rawResponse ?? string.Empty;
            }

            return result;
        }
    }
}
