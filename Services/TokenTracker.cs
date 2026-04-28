// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System.Threading;

namespace BIBIM_MVP
{
    /// <summary>
    /// Tracks LLM token usage per session for logging purposes.
    /// Cache-aware so prompt-caching effectiveness can be measured at runtime.
    /// </summary>
    public static class TokenTracker
    {
        private static int _sessionInputTokens;
        private static int _sessionOutputTokens;
        private static int _sessionCacheCreationTokens;
        private static int _sessionCacheReadTokens;
        private static int _sessionCallCount;

        public static int SessionInputTokens => _sessionInputTokens;
        public static int SessionOutputTokens => _sessionOutputTokens;
        public static int SessionCacheCreationTokens => _sessionCacheCreationTokens;
        public static int SessionCacheReadTokens => _sessionCacheReadTokens;
        public static int SessionCallCount => _sessionCallCount;

        /// <summary>
        /// Cache hit ratio = cache_read / (input + cache_read). Returns 0 if no input recorded.
        /// </summary>
        public static double SessionCacheHitRatio
        {
            get
            {
                int total = _sessionInputTokens + _sessionCacheReadTokens;
                return total == 0 ? 0.0 : (double)_sessionCacheReadTokens / total;
            }
        }

        /// <summary>
        /// Records token usage for a single LLM call. <paramref name="cacheCreation"/> and
        /// <paramref name="cacheRead"/> default to 0 so non-Anthropic providers (or providers
        /// without cache support) can call the simpler 5-arg overload.
        /// </summary>
        public static void Track(string callType, string provider, string model,
            int inputTokens, int outputTokens,
            string requestId = null,
            int cacheCreation = 0, int cacheRead = 0)
        {
            Interlocked.Add(ref _sessionInputTokens, inputTokens);
            Interlocked.Add(ref _sessionOutputTokens, outputTokens);
            Interlocked.Add(ref _sessionCacheCreationTokens, cacheCreation);
            Interlocked.Add(ref _sessionCacheReadTokens, cacheRead);
            Interlocked.Increment(ref _sessionCallCount);

            Logger.Log("TokenTracker",
                $"rid={requestId} type={callType} provider={provider} in={inputTokens} out={outputTokens} " +
                $"cache_create={cacheCreation} cache_read={cacheRead} " +
                $"session_total_in={SessionInputTokens} session_total_out={SessionOutputTokens} " +
                $"session_cache_create={SessionCacheCreationTokens} session_cache_read={SessionCacheReadTokens}");
        }

        public static void ResetSession()
        {
            Interlocked.Exchange(ref _sessionInputTokens, 0);
            Interlocked.Exchange(ref _sessionOutputTokens, 0);
            Interlocked.Exchange(ref _sessionCacheCreationTokens, 0);
            Interlocked.Exchange(ref _sessionCacheReadTokens, 0);
            Interlocked.Exchange(ref _sessionCallCount, 0);
        }
    }
}
