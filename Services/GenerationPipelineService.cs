using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BIBIM_MVP
{
    /// <summary>
    /// Orchestrates the code generation pipeline (RAG → Claude → Verify → Validate → AutoFix).
    /// Extracted from ChatWorkspaceViewModel to reduce God Object complexity.
    ///
    /// Responsibilities:
    ///   - Invoke GeminiService.GetResponseAsync with phase progress reporting
    ///   - Translate phase keys into localized StatusText updates via the onStatusChanged callback
    ///   - Keep ChatWorkspaceViewModel free of pipeline wiring concerns
    ///
    /// NOT responsible for:
    ///   - Spec generation / revision (SpecGenerator handles this)
    ///   - History persistence (HistoryManager / LocalSessionManager)
    ///   - Node injection into Dynamo canvas (NodeManipulator)
    ///   - UI rendering (ChatWorkspaceViewModel / Views)
    /// </summary>
    internal sealed class GenerationPipelineService
    {
        private readonly Action<string> _onStatusChanged;

        /// <summary>
        /// Phase-to-i18n-key mapping. Keys are emitted by GeminiService.GetResponseAsync.
        /// </summary>
        private static readonly Dictionary<string, string> PhaseToI18nKey = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "rag",      "Pipeline_Phase_Rag"      },
            { "code",     "Pipeline_Phase_Code"     },
            { "verify",   "Pipeline_Phase_Verify"   },
            { "validate", "Pipeline_Phase_Validate" },
            { "autofix",  "Pipeline_Phase_Autofix"  },
        };

        /// <param name="onStatusChanged">
        /// Callback invoked on each phase transition. Receives a localized status string.
        /// Should update ChatWorkspaceViewModel.StatusText on the UI thread.
        /// </param>
        public GenerationPipelineService(Action<string> onStatusChanged)
        {
            _onStatusChanged = onStatusChanged ?? throw new ArgumentNullException(nameof(onStatusChanged));
        }

        /// <summary>
        /// Runs the full generation pipeline and returns the raw response string from GeminiService.
        /// </summary>
        /// <param name="history">Conversation history including the current user message.</param>
        /// <param name="requestId">Correlation ID for logging.</param>
        /// <param name="cancellationToken">Propagated from the ViewModel cancel button.</param>
        public Task<GenerationResult> RunAsync(
            IEnumerable<ChatMessage> history,
            string requestId,
            CancellationToken cancellationToken)
        {
            return GeminiService.GetResponseAsync(
                history,
                requestId,
                cancellationToken,
                OnPhaseChanged);
        }

        private void OnPhaseChanged(string phase)
        {
            string i18nKey;
            if (!PhaseToI18nKey.TryGetValue(phase, out i18nKey))
                i18nKey = "ViewModel_LoadingGenerating";

            string localizedText = LocalizationService.Get(i18nKey);
            _onStatusChanged(localizedText);
        }
    }
}
