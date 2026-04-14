using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace BIBIM_MVP
{
    internal static class ValidationMetricsService
    {
        private static readonly ConcurrentDictionary<string, long> Counters =
            new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        public static void Track(string finalStatus, int blockCount, int warnCount, int symbolsTotal, int fixAttempts)
        {
            Increment("validation.total");
            Increment("validation.status." + (finalStatus ?? "unknown"));

            if (blockCount > 0) IncrementBy("validation.blocks", blockCount);
            if (warnCount > 0) IncrementBy("validation.warnings", warnCount);
            if (symbolsTotal > 0) IncrementBy("validation.symbols", symbolsTotal);
            if (fixAttempts > 0) IncrementBy("validation.fix_attempts", fixAttempts);
        }

        public static string Snapshot()
        {
            var pairs = Counters
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + kv.Value)
                .ToArray();

            return string.Join(",", pairs);
        }

        private static void Increment(string key)
        {
            Counters.AddOrUpdate(key, 1, (_, oldValue) => oldValue + 1);
        }

        private static void IncrementBy(string key, int value)
        {
            Counters.AddOrUpdate(key, value, (_, oldValue) => oldValue + value);
        }
    }
}
