// Copyright (c) 2026 SquareZero Inc. — Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BIBIM_MVP
{
    /// <summary>
    /// Pure C# BM25 search engine. No NuGet dependencies.
    ///
    /// Used by LocalDynamoRagService to search the in-memory Revit API index.
    /// BM25 parameters: k1=1.5, b=0.75 (standard defaults).
    ///
    /// Strengths: exact API name matching (PDFExportOptions, FilteredElementCollector, etc.)
    /// Limitations: natural-language queries with no keyword overlap (Phase 2: semantic layer).
    /// </summary>
    internal class BM25Engine
    {
        private const double K1 = 1.5;
        private const double B = 0.75;

        private readonly List<RagChunk> _chunks;
        private readonly Dictionary<string, List<(int idx, int tf)>> _invertedIndex;
        private readonly int[] _chunkLengths;
        private readonly double _avgChunkLength;
        private readonly Dictionary<string, double> _idfCache;

        public int ChunkCount => _chunks.Count;

        public BM25Engine(List<RagChunk> chunks)
        {
            _chunks = chunks ?? throw new ArgumentNullException("chunks");
            _invertedIndex = new Dictionary<string, List<(int, int)>>(StringComparer.OrdinalIgnoreCase);
            _idfCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _chunkLengths = new int[chunks.Count];
            BuildIndex();

            long total = 0;
            for (int i = 0; i < _chunkLengths.Length; i++) total += _chunkLengths[i];
            _avgChunkLength = chunks.Count > 0 ? (double)total / chunks.Count : 1.0;
        }

        public List<RagChunk> Search(string query, int topK = 5)
        {
            if (string.IsNullOrWhiteSpace(query) || _chunks.Count == 0)
                return new List<RagChunk>();

            var queryTokens = Tokenize(query);
            if (queryTokens.Count == 0) return new List<RagChunk>();

            var scores = new double[_chunks.Count];

            foreach (string token in queryTokens.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_invertedIndex.TryGetValue(token, out var postings)) continue;
                double idf = GetIdf(token, postings.Count);
                foreach (var (idx, tf) in postings)
                {
                    double dl = _chunkLengths[idx];
                    double tfNorm = (tf * (K1 + 1.0)) / (tf + K1 * (1.0 - B + B * dl / _avgChunkLength));
                    scores[idx] += idf * tfNorm;
                }
            }

            var results = new List<(int idx, double score)>();
            for (int i = 0; i < scores.Length; i++)
                if (scores[i] > 0) results.Add((i, scores[i]));

            results.Sort((a, b2) => b2.score.CompareTo(a.score));
            return results.Take(topK).Select(r => _chunks[r.idx]).ToList();
        }

        private void BuildIndex()
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                var tokens = Tokenize(_chunks[i].IndexText);
                _chunkLengths[i] = tokens.Count;

                var tfMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (string token in tokens)
                {
                    if (!tfMap.TryGetValue(token, out int count)) tfMap[token] = 1;
                    else tfMap[token] = count + 1;
                }

                foreach (var kv in tfMap)
                {
                    if (!_invertedIndex.TryGetValue(kv.Key, out var list))
                    {
                        list = new List<(int, int)>();
                        _invertedIndex[kv.Key] = list;
                    }
                    list.Add((i, kv.Value));
                }
            }
        }

        private double GetIdf(string token, int docFreq)
        {
            if (_idfCache.TryGetValue(token, out double cached)) return cached;
            int n = _chunks.Count;
            double idf = Math.Log((n - docFreq + 0.5) / (docFreq + 0.5) + 1.0);
            _idfCache[token] = idf;
            return idf;
        }

        internal static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            var raw = Regex.Split(text, @"[^a-zA-Z0-9_]+");
            var result = new List<string>(raw.Length * 2);
            foreach (string token in raw)
            {
                if (token.Length < 2) continue;
                result.Add(token);
                var sub = SplitCamelCase(token);
                if (sub.Count > 1) result.AddRange(sub);
            }
            return result;
        }

        private static List<string> SplitCamelCase(string token)
        {
            var parts = Regex.Split(token, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])");
            var result = new List<string>();
            foreach (string p in parts)
                if (p.Length >= 2) result.Add(p);
            return result;
        }
    }

    internal class RagChunk
    {
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public string DisplayText { get; set; }
        public string IndexText { get; set; }
    }
}
