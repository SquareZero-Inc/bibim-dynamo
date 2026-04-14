using System.Threading;

namespace BIBIM_MVP
{
    /// <summary>
    /// Tracks LLM token usage per session for logging purposes.
    /// </summary>
    public static class TokenTracker
    {
        private static int _sessionInputTokens;
        private static int _sessionOutputTokens;
        private static int _sessionCallCount;
        public static int SessionInputTokens => _sessionInputTokens;
        public static int SessionOutputTokens => _sessionOutputTokens;
        public static int SessionCallCount => _sessionCallCount;

        public static void Track(string callType, string provider, string model,
            int inputTokens, int outputTokens, string requestId = null)
        {
            Interlocked.Add(ref _sessionInputTokens, inputTokens);
            Interlocked.Add(ref _sessionOutputTokens, outputTokens);
            Interlocked.Increment(ref _sessionCallCount);

            Logger.Log("TokenTracker",
                $"rid={requestId} type={callType} provider={provider} in={inputTokens} out={outputTokens} " +
                $"session_total_in={SessionInputTokens} session_total_out={SessionOutputTokens}");
        }

        public static void ResetSession()
        {
            Interlocked.Exchange(ref _sessionInputTokens, 0);
            Interlocked.Exchange(ref _sessionOutputTokens, 0);
            Interlocked.Exchange(ref _sessionCallCount, 0);
        }
    }
}
