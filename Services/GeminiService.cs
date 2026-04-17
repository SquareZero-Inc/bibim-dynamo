// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BIBIM_MVP
{
    /// <summary>
    /// Orchestrates the full code-generation pipeline:
    ///   RAG fetch (RagService) → Claude codegen (ClaudeApiClient) →
    ///   Gemini verify (RagService) → local validation gate.
    ///
    /// RAG, Claude API, and token-tracking concerns live in
    /// <see cref="RagService"/> and <see cref="ClaudeApiClient"/> respectively.
    /// </summary>
    internal static class GeminiService
    {
        private static void LogPerf(string requestId, string step, long elapsedMs, string detail = null)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return;
            string suffix = string.IsNullOrWhiteSpace(detail) ? "" : $" detail={detail}";
            Logger.Log("GeminiService", $"[PERF] rid={requestId} step={step} ms={elapsedMs}{suffix}");
        }

        private static string ClipForLog(string text, int maxLen = 180)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string normalized = text.Replace("", " ").Replace("
", " ");
            if (normalized.Length <= maxLen) return normalized;
            return normalized.Substring(0, maxLen) + "...";
        }

        private const string ValidationBlockPrefix = "TYPE: VALIDATION_BLOCK|";

        private static async Task<string> ApplyLocalValidationGateAsync(
            string responseText,
            string revitVersion,
            string dynamoVersion,
            string requestId,
            ConfigService.RagConfig config,
            string claudeApiKey,
            string claudeModel,
            Action<string> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(responseText) || !responseText.Contains("TYPE: CODE|"))
                return responseText;

            int codeStart = responseText.IndexOf("TYPE: CODE|", StringComparison.Ordinal) + "TYPE: CODE|".Length;
            int guideStart = responseText.IndexOf("TYPE: GUIDE|", StringComparison.Ordinal);
            string codeSection;
            string guideSection = "";

            if (guideStart > 0)
            {
                codeSection = responseText.Substring(codeStart, guideStart - codeStart).Trim();
                guideSection = responseText.Substring(guideStart).Trim();
            }
            else
            {
                codeSection = responseText.Substring(codeStart).Trim();
            }

            ValidationOptions options = new ValidationOptions
            {
                RolloutPhase = config == null ? "phase3" : config.ValidationRolloutPhase,
                EnableApiXmlHints = config == null || config.EnableApiXmlHints,
                CurrentFixAttempt = 0
            };

            var sw = Stopwatch.StartNew();
            LocalValidationResult validation = LocalCodeValidationService.ValidateAndFix(codeSection, revitVersion, options);
            sw.Stop();

            string detail = validation.IsPass ? (validation.CodeChanged ? "pass:auto-fixed" : "pass") : "blocked";
            detail += $":block={validation.BlockCount}:warn={validation.WarningCount}:index={validation.ApiIndexStatus}:xml={validation.XmlIndexStatus}:symbols={validation.SymbolsTotal}:unknown={validation.UnknownSymbols}";
            LogPerf(requestId, "local-validate", sw.ElapsedMilliseconds, detail);
            LogEnumValidationDetails(requestId, validation, "initial");

            string currentCode = codeSection;
            int attemptedFixes = 0;
            int maxFixAttempts = config == null ? 0 : config.AutoFixMaxAttempts;
            bool autoFixEnabled = config != null && config.AutoFixEnabled;

            while (!validation.IsPass &&
                   autoFixEnabled &&
                   maxFixAttempts > 0 &&
                   attemptedFixes < maxFixAttempts &&
                   !string.IsNullOrWhiteSpace(claudeApiKey) &&
                   !string.IsNullOrWhiteSpace(claudeModel))
            {
                attemptedFixes++;
                onProgress?.Invoke("autofix");

                var autoFixSw = Stopwatch.StartNew();
                string prompt = AutoFixRequestBuilder.BuildPrompt(currentCode, validation, revitVersion, attemptedFixes, maxFixAttempts);
                string fixedText = await ClaudeApiClient.RequestValidationAutoFixAsync(claudeApiKey, claudeModel, prompt, requestId, attemptedFixes, cancellationToken);
                autoFixSw.Stop();

                LogPerf(requestId, "validation-autofix", autoFixSw.ElapsedMilliseconds, $"attempt={attemptedFixes}:len={fixedText?.Length ?? 0}");

                if (string.IsNullOrWhiteSpace(fixedText))
                {
                    Logger.Log("GeminiService", $"[VALIDATION][ENUM] rid={requestId} phase=autofix-{attemptedFixes} result=empty_response");
                    break;
                }

                currentCode = StripCodeFences(fixedText).Trim();
                options.CurrentFixAttempt = attemptedFixes;
                validation = LocalCodeValidationService.ValidateAndFix(currentCode, revitVersion, options);
                LogEnumValidationDetails(requestId, validation, $"autofix-{attemptedFixes}");
            }

            ValidationMetricsService.Track(validation.FinalStatus, validation.BlockCount, validation.WarningCount, validation.SymbolsTotal, attemptedFixes);
            Logger.Log(
                "GeminiService",
                $"[VALIDATION] rid={requestId} symbols_total={validation.SymbolsTotal} block_count={validation.BlockCount} warn_count={validation.WarningCount} unknown_symbols={validation.UnknownSymbols} fix_attempts={attemptedFixes} final_status={validation.FinalStatus}");

            if (validation.IsPass)
            {
                string outputCode = validation.ValidatedCode;
                if (string.IsNullOrWhiteSpace(outputCode))
                    outputCode = currentCode;

                if ((validation.CodeChanged || attemptedFixes > 0) && !string.IsNullOrWhiteSpace(outputCode))
                {
                    Logger.Log("GeminiService", $"[VALIDATION] rid={requestId} local/autofix applied attempts={attemptedFixes}");

                    if (!string.IsNullOrWhiteSpace(guideSection))
                        return $"TYPE: CODE|{outputCode}
{guideSection}";

                    return $"TYPE: CODE|{outputCode}";
                }

                return responseText;
            }

            Logger.Log(
                "GeminiService",
                $"[VALIDATION] rid={requestId} blocked block_count={validation.BlockCount} warn_count={validation.WarningCount}");
            var blockedSymbols = validation.Issues
                .Where(i => i.Severity == ValidationSeverity.Block)
                .Select(i => i.Symbol)
                .Distinct()
                .Take(10);
            Logger.Log("GeminiService",
                $"[VALIDATION] rid={requestId} blocked_symbols={string.Join(",", blockedSymbols)} symbols_total={validation.SymbolsTotal} unknown_symbols={validation.UnknownSymbols} fix_attempts={attemptedFixes} final_status={validation.FinalStatus}");
            Logger.Log("GeminiService", $"[VALIDATION] metrics_snapshot={ValidationMetricsService.Snapshot()}");
            return BuildValidationBlockedResponse(validation, attemptedFixes);
        }

        private static string BuildValidationBlockedResponse(LocalValidationResult validation, int fixAttempts)
        {
            var sb = new StringBuilder();
            sb.AppendLine(LocalizationService.Get("ValidationBlock_Header"));
            sb.AppendLine(LocalizationService.Get("ValidationBlock_Summary"));

            var blockIssues = validation.Issues
                .Where(i => i.Severity == ValidationSeverity.Block)
                .Take(8)
                .ToList();

            if (blockIssues.Count == 0)
            {
                // IsPass==false with no Block issues means only Warning-severity issues triggered failure.
                // Show those instead of a misleading "parse failed" message.
                var warningIssues = validation.Issues?
                    .Where(i => i.Severity == ValidationSeverity.Warning)
                    .Take(8)
                    .ToList() ?? new List<ValidationIssue>();

                if (warningIssues.Count > 0)
                {
                    foreach (var issue in warningIssues)
                    {
                        string line = $"- [{issue.Category}] {issue.Symbol}: {issue.Message}";
                        if (issue.Candidates != null && issue.Candidates.Count > 0)
                        {
                            line += $" ({LocalizationService.Get("ValidationBlock_Candidates")}: {string.Join(", ", issue.Candidates.Take(3))})";
                        }
                        sb.AppendLine(line);
                        if (!string.IsNullOrEmpty(issue.FixSuggestion))
                        {
                            sb.AppendLine($"  → {issue.FixSuggestion}");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("- " + LocalizationService.Get("ValidationBlock_ParseFailed"));
                }
            }
            else
            {
                foreach (var issue in blockIssues)
                {
                    string line = $"- [{issue.Category}] {issue.Symbol}: {issue.Message}";
                    if (issue.Candidates != null && issue.Candidates.Count > 0)
                    {
                        line += $" ({LocalizationService.Get("ValidationBlock_Candidates")}: {string.Join(", ", issue.Candidates.Take(3))})";
                    }
                    sb.AppendLine(line);
                    if (!string.IsNullOrEmpty(issue.FixSuggestion))
                    {
                        sb.AppendLine($"  → {issue.FixSuggestion}");
                    }
                }
            }

            if (validation.AutoFixApplied || fixAttempts > 0)
            {
                sb.AppendLine(LocalizationService.Format("ValidationBlock_AutoFixNote", fixAttempts));
            }

            sb.AppendLine(LocalizationService.Get("ValidationBlock_Actions"));
            sb.AppendLine(LocalizationService.Get("ValidationBlock_Action1"));
            sb.AppendLine(LocalizationService.Get("ValidationBlock_Action2"));
            sb.AppendLine(LocalizationService.Get("ValidationBlock_Action3"));
            return ValidationBlockPrefix + sb.ToString().Trim();
        }

        private static void LogEnumValidationDetails(string requestId, LocalValidationResult validation, string phase)
        {
            if (validation == null)
                return;

            var referencedBip = validation.ReferencedBuiltInParameters ?? new List<string>();
            var referencedBic = validation.ReferencedBuiltInCategories ?? new List<string>();
            var referencedUnit = validation.ReferencedUnitTypeIds ?? new List<string>();
            var missingBip = validation.MissingBuiltInParameters ?? new List<string>();
            var missingBic = validation.MissingBuiltInCategories ?? new List<string>();
            var missingUnit = validation.MissingUnitTypeIds ?? new List<string>();

            Logger.Log(
                "GeminiService",
                $"[VALIDATION][ENUM] rid={requestId} phase={phase} refs:bip={referencedBip.Count},bic={referencedBic.Count},unit={referencedUnit.Count} missing:bip={missingBip.Count},bic={missingBic.Count},unit={missingUnit.Count}");

            Logger.Log("GeminiService", $"[VALIDATION][ENUM] rid={requestId} phase={phase} ref_bip={FormatListForLog(referencedBip)}");
            Logger.Log("GeminiService", $"[VALIDATION][ENUM] rid={requestId} phase={phase} ref_bic={FormatListForLog(referencedBic)}");
            Logger.Log("GeminiService", $"[VALIDATION][ENUM] rid={requestId} phase={phase} ref_unit={FormatListForLog(referencedUnit)}");
            Logger.Log("GeminiService", $"[VALIDATION][ENUM] rid={requestId} phase={phase} missing_bip={FormatListForLog(missingBip)}");
            Logger.Log("GeminiService", $"[VALIDATION][ENUM] rid={requestId} phase={phase} missing_bic={FormatListForLog(missingBic)}");
            Logger.Log("GeminiService", $"[VALIDATION][ENUM] rid={requestId} phase={phase} missing_unit={FormatListForLog(missingUnit)}");

            var enumIssues = validation.Issues == null
                ? new List<ValidationIssue>()
                : validation.Issues
                    .Where(i =>
                        string.Equals(i.Category, "BuiltInParameter", StringComparison.Ordinal) ||
                        string.Equals(i.Category, "BuiltInCategory", StringComparison.Ordinal) ||
                        string.Equals(i.Category, "UnitTypeId", StringComparison.Ordinal))
                    .ToList();

            if (enumIssues.Count == 0)
            {
                Logger.Log("GeminiService", $"[VALIDATION][ENUM] rid={requestId} phase={phase} issues=none");
                return;
            }

            foreach (var issue in enumIssues.Take(50))
            {
                string candidateText = issue.Candidates == null || issue.Candidates.Count == 0
                    ? "(none)"
                    : string.Join(", ", issue.Candidates.Take(5));

                Logger.Log(
                    "GeminiService",
                    $"[VALIDATION][ENUM] rid={requestId} phase={phase} issue={issue.Category}.{issue.Symbol} severity={issue.Severity} msg={ClipForLog(issue.Message, 280)} candidates={candidateText}");
            }
        }

        private static string FormatListForLog(IEnumerable<string> values, int maxItems = 40)
        {
            if (values == null)
                return "(none)";

            var list = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();

            if (list.Count == 0)
                return "(none)";

            if (list.Count <= maxItems)
                return string.Join(", ", list);

            return string.Join(", ", list.Take(maxItems)) + $" ... (+{list.Count - maxItems})";
        }

        private static string StripCodeFences(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed;

            int firstLineBreak = trimmed.IndexOf('
');
            if (firstLineBreak < 0)
                return trimmed.Replace("```", "").Trim();

            string body = trimmed.Substring(firstLineBreak + 1);
            if (body.EndsWith("```", StringComparison.Ordinal))
                body = body.Substring(0, body.Length - 3);

            return body.Trim();
        }

        /// <summary>
        /// Returns current RAG configuration info for display in UI
        /// Delegates to ConfigService for centralized config management
        /// </summary>
        internal static (string revitVersion, string dynamoVersion, string ragStoreName, string model) GetCurrentConfigInfo()
        {
            return ConfigService.GetDisplayInfo();
        }

        /// <summary>
        /// Scans conversation history for Graph Analysis results and extracts
        /// relevant diagnostic findings for code generation context injection.
        /// Parses the actual Analysis response format: ## Diagnosis (🔴/🟡 sections)
        /// and ## Solutions, extracting error→fix pairs.
        /// Returns formatted context string or empty if no relevant analysis found.
        /// </summary>
        private static string ExtractAnalysisContext(IEnumerable<ChatMessage> history, string currentSpec)
        {
            if (history == null || string.IsNullOrWhiteSpace(currentSpec))
                return "";

            var analyses = new List<string>();
            string specLower = currentSpec.ToLowerInvariant();

            // Scan history in reverse for assistant messages containing analysis markers
            var historyList = history.ToList();
            for (int i = historyList.Count - 1; i >= 0 && analyses.Count < 3; i--)
            {
                var msg = historyList[i];
                if (msg.IsUser || string.IsNullOrWhiteSpace(msg.Text))
                    continue;

                string text = msg.Text;
                // Look for Graph Analysis result markers (EN and KR formats)
                bool hasAnalysis = text.Contains("## Diagnosis") ||
                                   text.Contains("## Solutions") ||
                                   text.Contains("## 진단") ||
                                   text.Contains("## 해결") ||
                                   text.Contains("## 문제 진단") ||
                                   text.Contains("## 해결 솔루션") ||
                                   text.Contains("## Python Code Review") ||
                                   text.Contains("## Python 코드 검증");
                if (!hasAnalysis)
                    continue;

                // Extract diagnostic findings from actual Analysis response format
                var lines = text.Split('
');
                var findings = new List<string>();
                string currentSection = null;
                string currentError = null;
                var currentErrorLines = new List<string>();
                var solutionLines = new List<string>();
                bool inSolutionSection = false;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    // Track which section we're in
                    if (trimmed.StartsWith("## Solutions") || trimmed.StartsWith("## 해결"))
                    {
                        // Flush any pending error
                        FlushErrorBlock(findings, currentError, currentErrorLines);
                        currentError = null;
                        currentErrorLines.Clear();
                        inSolutionSection = true;
                        currentSection = "solutions";
                        continue;
                    }
                    if (trimmed.StartsWith("## Diagnosis") || trimmed.StartsWith("## 문제 진단") || trimmed.StartsWith("## 진단"))
                    {
                        currentSection = "diagnosis";
                        inSolutionSection = false;
                        continue;
                    }
                    if (trimmed.StartsWith("## Python Code Review") || trimmed.StartsWith("## Python 코드 검증"))
                    {
                        FlushErrorBlock(findings, currentError, currentErrorLines);
                        currentError = null;
                        currentErrorLines.Clear();
                        currentSection = "codereview";
                        inSolutionSection = false;
                        continue;
                    }
                    if (trimmed.StartsWith("## "))
                    {
                        FlushErrorBlock(findings, currentError, currentErrorLines);
                        currentError = null;
                        currentErrorLines.Clear();
                        currentSection = "other";
                        inSolutionSection = false;
                        continue;
                    }

                    // In Diagnosis section: capture 🔴/🟡 errors and their details
                    if (currentSection == "diagnosis" || currentSection == "codereview")
                    {
                        // New error block starts with severity emoji or numbered bold header
                        if (trimmed.StartsWith("🔴") || trimmed.StartsWith("🟡") ||
                            (trimmed.StartsWith("**") && trimmed.Contains("Node:")) ||
                            (trimmed.StartsWith("**") && (trimmed.Contains("Error") || trimmed.Contains("오류"))))
                        {
                            FlushErrorBlock(findings, currentError, currentErrorLines);
                            currentError = trimmed;
                            currentErrorLines.Clear();
                        }
                        // Detail lines under current error
                        else if (currentError != null &&
                                 (trimmed.StartsWith("- **") || trimmed.StartsWith("- ") ||
                                  trimmed.StartsWith("❌") || trimmed.StartsWith("✅") ||
                                  trimmed.Contains("Error:") || trimmed.Contains("오류:") ||
                                  trimmed.Contains("Analysis:") || trimmed.Contains("Fix:") ||
                                  trimmed.Contains("수정:") || trimmed.Contains("분석:")))
                        {
                            currentErrorLines.Add(trimmed);
                        }
                    }

                    // In Solutions section: capture fix descriptions
                    if (inSolutionSection)
                    {
                        if (trimmed.StartsWith("**Fix") || trimmed.StartsWith("**수정") ||
                            trimmed.StartsWith("- **Fix") || trimmed.StartsWith("- **수정") ||
                            trimmed.StartsWith("✅") ||
                            (trimmed.StartsWith("**") && (trimmed.Contains("Fix") || trimmed.Contains("수정"))))
                        {
                            solutionLines.Add(trimmed);
                        }
                        else if (trimmed.StartsWith("- ") && solutionLines.Count > 0)
                        {
                            // Sub-detail of a solution
                            solutionLines.Add(trimmed);
                        }
                    }
                }

                // Flush last pending error
                FlushErrorBlock(findings, currentError, currentErrorLines);

                // Add solution lines as findings too
                foreach (var sl in solutionLines)
                {
                    findings.Add(sl);
                }

                if (findings.Count == 0)
                    continue;

                // Keyword relevance check — broader keyword set covering common Revit API domains
                string analysisLower = string.Join(" ", findings).ToLowerInvariant();
                string[] keywords = {
                    // EN keywords
                    "schedule", "export", "dwg", "filter", "collector",
                    "element", "view", "transaction", "parameter",
                    "wall", "floor", "ceiling", "door", "window", "room",
                    "family", "type", "instance", "level", "category",
                    "solid", "cut", "geometry", "curve", "line", "point",
                    "node", "python", "script", "api", "method", "signature",
                    "document", "doc", "error", "typeerror", "argumentexception",
                    // KR keywords
                    "스케줄", "내보내기", "필터", "요소", "뷰", "트랜잭션",
                    "벽", "바닥", "천장", "문", "창문", "룸",
                    "패밀리", "타입", "인스턴스", "레벨", "카테고리",
                    "솔리드", "커팅", "지오메트리", "커브", "노드", "오류"
                };
                bool hasRelevantKeyword = false;
                foreach (var kw in keywords)
                {
                    if (specLower.Contains(kw) && analysisLower.Contains(kw))
                    {
                        hasRelevantKeyword = true;
                        break;
                    }
                }

                if (!hasRelevantKeyword)
                    continue;

                analyses.Add(string.Join("
", findings));
            }

            if (analyses.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("[Previous Analysis — VERIFIED diagnostic from user's actual Revit environment]");
            sb.AppendLine("These findings are GROUND TRUTH. Apply them when generating code for this task.");
            for (int i = 0; i < analyses.Count; i++)
            {
                sb.AppendLine($"Analysis {i + 1}:");
                sb.AppendLine(analyses[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Helper: flushes accumulated error block lines into findings list.
        /// </summary>
        private static void FlushErrorBlock(List<string> findings, string errorHeader, List<string> detailLines)
        {
            if (string.IsNullOrEmpty(errorHeader))
                return;

            if (detailLines.Count > 0)
            {
                findings.Add($"{errorHeader}: {string.Join(" | ", detailLines)}");
            }
            else
            {
                findings.Add(errorHeader);
            }
        }


        internal static async Task<GenerationResult> GetResponseAsync(IEnumerable<ChatMessage> history, string requestId = null, CancellationToken cancellationToken = default, Action<string> onProgress = null)
        {
            var totalSw = Stopwatch.StartNew();
            try
            {
                var config = ConfigService.GetRagConfig();
                string ragStore = config.RagStore;
                string ragFallbackStore = config.FallbackStore;
                string revitVersion = config.RevitVersion;
                string dynamoVersion = config.DynamoVersion;
                string claudeModel = config.ClaudeModel;
                string geminiModel = config.GeminiModel;
                
                string claudeApiKey = ClaudeApiClient.GetClaudeApiKey();
                string geminiApiKey = RagService.GetGeminiApiKey();
                
                string lastUserMessage = "";
                foreach (var msg in history)
                {
                    if (msg.IsUser)
                        lastUserMessage = msg.Text;
                }
                
                bool isSpecBasedRequest = lastUserMessage.Contains("[SPEC_CONFIRMED|");
                Logger.Log("GeminiService", $"[SPEC_TRIGGER] rid={requestId} matched={isSpecBasedRequest}");
                
                string apiDocContext = "";
                bool ragFailed = false;
                string ragFailReason = "";
                int ragTimeoutSeconds = config.RagTimeoutSeconds > 0 ? config.RagTimeoutSeconds : 25;
                if (isSpecBasedRequest && !string.IsNullOrEmpty(geminiApiKey) && !string.IsNullOrEmpty(ragStore))
                {
                    onProgress?.Invoke("rag");
                    var ragSw = Stopwatch.StartNew();

                    // Build cache key from store + extracted keywords (more stable than full spec text)
                    string ragCacheKey = RagService.BuildCacheKey(ragStore, lastUserMessage);
                    RagFetchResult ragResult;
                    bool cacheHit = RagService.TryGetCache(ragCacheKey, out ragResult);

                    if (cacheHit)
                    {
                        Logger.Log("GeminiService", $"[RAG_CACHE_HIT] rid={requestId} key_len={ragCacheKey.Length}");
                    }
                    else
                    {
                        // First attempt: full spec text for maximum context
                        ragResult = await RagService.FetchRelevantApiDocsAsync(geminiApiKey, lastUserMessage, ragStore, revitVersion, geminiModel, requestId, ragTimeoutSeconds);

                        // Retry with keyword-only query (more focused, different angle)
                        if (!ragResult.HasContext && ragResult.Status != "no_match")
                        {
                            string keywordQuery = RagService.ExtractRagKeywords(lastUserMessage);
                            Logger.Log("GeminiService", $"[RAG_RETRY] rid={requestId} first_status={ragResult.Status}, retrying with keywords: {ClipForLog(keywordQuery, 120)}");
                            ragResult = await RagService.FetchRelevantApiDocsAsync(geminiApiKey, keywordQuery, ragStore, revitVersion, geminiModel, requestId, ragTimeoutSeconds);
                            if (!ragResult.HasContext)
                            {
                                Logger.Log("GeminiService", $"[RAG_RETRY] rid={requestId} second_status={ragResult.Status}, proceeding without RAG");
                            }
                        }

                        // Fallback store retry: if primary store fails and a fallback is configured (e.g. R2027 → R2026)
                        if (!ragResult.HasContext && !string.IsNullOrEmpty(ragFallbackStore) && ragFallbackStore != ragStore)
                        {
                            Logger.Log("GeminiService", $"[RAG_FALLBACK] rid={requestId} primary_store_failed status={ragResult.Status}, retrying with fallback store");
                            ragResult = await RagService.FetchRelevantApiDocsAsync(geminiApiKey, lastUserMessage, ragFallbackStore, revitVersion, geminiModel, requestId, ragTimeoutSeconds);
                            if (ragResult.HasContext)
                                Logger.Log("GeminiService", $"[RAG_FALLBACK] rid={requestId} fallback succeeded");
                        }

                        // Cache successful results for this session
                        if (ragResult.HasContext)
                            RagService.SetCache(ragCacheKey, ragResult);
                    }

                    apiDocContext = ragResult.ContextText;
                    ragFailed = !ragResult.HasContext && ragResult.Status != "no_match";
                    ragFailReason = ragResult.ErrorSummary;
                    ragSw.Stop();
                    LogPerf(requestId, "rag", ragSw.ElapsedMilliseconds, cacheHit ? "cache_hit" : ragResult.Status);
                }
                
                // Extract previous Graph Analysis context for code generation
                string analysisContext = "";
                if (isSpecBasedRequest)
                {
                    analysisContext = ExtractAnalysisContext(history, lastUserMessage);
                    if (!string.IsNullOrEmpty(analysisContext))
                    {
                        LogPerf(requestId, "analysis-context", 0, "injected");
                    }
                }

                if (string.IsNullOrEmpty(claudeApiKey))
                {
                    Logger.Log("GeminiService", $"[CONFIG_ERROR] rid={requestId} claude_api_key_not_configured: set claude_api_key in rag_config.json or CLAUDE_API_KEY env var");
                    return GenerationResult.Parse(LocalizationService.Get("Error_ServiceUnavailable"));
                }

                // 2단계: Claude로 코드 생성 (API 문서 컨텍스트 포함)
                onProgress?.Invoke("code");
                var codeSw = Stopwatch.StartNew();
                string initialResponse = await ClaudeApiClient.CallClaudeApiAsync(
                    claudeApiKey,
                    history,
                    revitVersion,
                    dynamoVersion,
                    claudeModel,
                    apiDocContext,
                    isSpecBasedRequest,
                    requestId,
                    cancellationToken,
                    analysisContext);
                codeSw.Stop();
                LogPerf(requestId, "code", codeSw.ElapsedMilliseconds, isSpecBasedRequest ? "spec-prompt" : "direct");
                bool enableVerifyStage = config.VerifyStageEnabled && !string.IsNullOrEmpty(geminiApiKey);
                int verifyTimeoutSeconds = config.VerifyTimeoutSeconds > 0 ? config.VerifyTimeoutSeconds : 30;

                // Optional verify stage: skip trivially short code (< 15 lines) to save latency
                if (enableVerifyStage && initialResponse.Contains("TYPE: CODE|"))
                {
                    int codeStart = initialResponse.IndexOf("TYPE: CODE|") + "TYPE: CODE|".Length;
                    int guideStart = initialResponse.IndexOf("TYPE: GUIDE|");

                    string codeSection;
                    if (guideStart > 0)
                    {
                        codeSection = initialResponse.Substring(codeStart, guideStart - codeStart).Trim();
                    }
                    else
                    {
                        codeSection = initialResponse.Substring(codeStart).Trim();
                    }

                    int codeLineCount = codeSection.Split('
').Length;
                    // Skip verify for short code OR simple read-only patterns (no Transaction = no side effects)
                    bool isSimpleReadOnly = !codeSection.Contains("Transaction") &&
                                           !codeSection.Contains("Set(") &&
                                           !codeSection.Contains("Create(") &&
                                           !codeSection.Contains("Delete(");
                    if (codeLineCount < 40 && isSimpleReadOnly)
                    {
                        Logger.Log("GeminiService", $"[VERIFY] rid={requestId} skipped: simple read-only code ({codeLineCount} lines)");
                        enableVerifyStage = false;
                    }
                    else if (codeLineCount < 15)
                    {
                        Logger.Log("GeminiService", $"[VERIFY] rid={requestId} skipped: trivial code ({codeLineCount} lines)");
                        enableVerifyStage = false;
                    }

                    if (enableVerifyStage)
                    {
                        onProgress?.Invoke("verify");
                    var verifySw = Stopwatch.StartNew();
                    var verifyResult = await RagService.VerifyAndFixCodeAsync(
                        geminiApiKey,
                        codeSection,
                        ragStore,
                        revitVersion,
                        dynamoVersion,
                        geminiModel,
                        requestId,
                        verifyTimeoutSeconds);
                    string verifiedCode = verifyResult.Code;
                    verifySw.Stop();
                    string verifyDetail;
                    if (verifyResult.Outcome == "ok")
                    {
                        verifyDetail = verifiedCode == codeSection ? "unchanged" : "updated";
                    }
                    else
                    {
                        verifyDetail = verifyResult.Outcome;
                    }
                    if (!string.IsNullOrWhiteSpace(verifyResult.Detail))
                    {
                        verifyDetail += $":{verifyResult.Detail}";
                    }
                    LogPerf(requestId, "verify", verifySw.ElapsedMilliseconds, verifyDetail);
                    
                    if (!string.IsNullOrEmpty(verifiedCode) && verifiedCode != codeSection)
                    {
                        if (guideStart > 0)
                        {
                            string guideSection = initialResponse.Substring(guideStart);
                            initialResponse = $"TYPE: CODE|{verifiedCode}
{guideSection}";
                        }
                        else
                        {
                            initialResponse = $"TYPE: CODE|{verifiedCode}";
                        }
                    }
                    } // end if (enableVerifyStage) after line-count check
                }

                if (config.ValidationGateEnabled)
                {
                    onProgress?.Invoke("validate");
                    initialResponse = await ApplyLocalValidationGateAsync(
                        initialResponse,
                        revitVersion,
                        dynamoVersion,
                        requestId,
                        config,
                        claudeApiKey,
                        claudeModel,
                        onProgress,
                        cancellationToken);
                }
                if (ragFailed)
                    Logger.Log("GeminiService", $"[RAG_FAILED] rid={requestId} reason={ragFailReason}");
                return GenerationResult.Parse(initialResponse);
            }
            catch (OperationCanceledException)
            {
                // 취소 예외는 다시 throw하여 상위에서 처리
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log("GeminiService", $"[SYSTEM_ERROR] rid={requestId} type={ex.GetType().Name} msg={ex.Message}");
                if (ex.InnerException != null)
                {
                    Logger.Log("GeminiService", $"[SYSTEM_ERROR] rid={requestId} inner={ex.InnerException.GetType().Name} inner_msg={ex.InnerException.Message}");
                }
                throw;
            }
            finally
            {
                totalSw.Stop();
                LogPerf(requestId, "gemini-total", totalSw.ElapsedMilliseconds);
            }
        }
        
    }
}
