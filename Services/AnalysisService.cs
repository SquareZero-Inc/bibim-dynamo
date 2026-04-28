// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
#if !NET48
using System.Text.Json;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Section 2: Smart Node Analysis Service
    /// Handles AI-powered graph analysis using Claude + Gemini RAG
    /// </summary>
    public class AnalysisService
    {
        private static readonly System.Text.RegularExpressions.Regex _actionButtonRegex =
            new System.Text.RegularExpressions.Regex(@"\[ACTION:([A-Z_]+)\|([^\]]+)\]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static void LogAnalysis(string requestId, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[BIBIM-Analysis] {message}");

            string ridPrefix = string.IsNullOrWhiteSpace(requestId) ? "" : $"rid={requestId} ";
            Logger.Log("AnalysisService", $"[GRAPH_ANALYSIS] {ridPrefix}{message}");
        }

        /// <summary>
        /// Analyze graph data and generate diagnostic report using Claude + Gemini RAG
        /// </summary>
        public static async Task<AnalysisResult> AnalyzeGraphAsync(GraphAnalysisData graphData, Action<int, string> progressCallback, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = new AnalysisResult();
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 8);
            LogAnalysis(requestId, $"AnalyzeGraphAsync start - workspace={graphData?.WorkspaceName ?? "Unknown"} nodes={graphData?.NodeCount ?? 0}");

            try
            {
                // Phase 1: 20% - Data preparation
                progressCallback?.Invoke(20, LocalizationService.Get("Analysis_DataExtraction"));

                if (graphData == null)
                {
                    LogAnalysis(requestId, "graphData is null");
                    result.Success = false;
                    result.ErrorMessage = LocalizationService.Format("Analysis_ErrorOccurred", "Graph data is null");
                    return result;
                }

                if (!string.IsNullOrEmpty(graphData.Error))
                {
                    LogAnalysis(requestId, $"graphData error: {graphData.Error}");
                    result.Success = false;
                    result.ErrorMessage = graphData.Error;
                    return result;
                }

                string graphJson = SerializeGraphData(graphData);
                LogAnalysis(requestId, $"graph JSON serialized - length={graphJson?.Length ?? 0}");

                var config = ConfigService.GetRagConfig();
                string revitVersion = config.RevitVersion;
                string dynamoVersion = config.DynamoVersion;

                // Phase 2: 40% - Local BM25 RAG over RevitAPI.xml
                progressCallback?.Invoke(40, LocalizationService.Get("Pipeline_Phase_Rag"));

                string ragContext = "";
                try
                {
                    string ragQueryText = BuildRagQueryText(graphData);
                    ragContext = await LocalDynamoRagService.FetchContextAsync(
                        ragQueryText, revitVersion, cancellationToken);
                    LogAnalysis(requestId, $"Local RAG context length={ragContext?.Length ?? 0}");
                }
                catch (Exception ragEx)
                {
                    LogAnalysis(requestId, $"Local RAG failed (non-fatal): {ragEx.Message}");
                    ragContext = string.Empty;
                }

                // Phase 3: 60% - Claude API call
                progressCallback?.Invoke(60, LocalizationService.Get("Analysis_AiProcessing"));

                string claudeApiKey = ClaudeApiClient.GetClaudeApiKey();
                if (string.IsNullOrEmpty(claudeApiKey))
                {
                    LogAnalysis(requestId, "Claude API key not found");
                    result.Success = false;
                    result.ErrorMessage = LocalizationService.Get("Analysis_ApiKeyNotFound");
                    return result;
                }

                string analysisPrompt = BuildAnalysisPrompt(graphJson, revitVersion, dynamoVersion);
                LogAnalysis(requestId, $"analysis prompt built - length={analysisPrompt?.Length ?? 0}");

                var conversation = new List<ChatMessage>
                {
                    new ChatMessage { IsUser = true, Text = analysisPrompt }
                };

                LogAnalysis(requestId, $"calling Claude - model={config.ClaudeModel}");
                string response = await ClaudeApiClient.CallClaudeApiAsync(
                    claudeApiKey,
                    conversation,
                    revitVersion,
                    dynamoVersion,
                    config.ClaudeModel,
                    apiDocContext: ragContext,
                    isCodeGeneration: false,
                    requestId: requestId,
                    cancellationToken: cancellationToken,
                    callType: "graph_analysis");

                LogAnalysis(requestId, $"Claude response received - length={response?.Length ?? 0}");

                // Phase 4: 90% - Parse response
                progressCallback?.Invoke(90, LocalizationService.Get("Analysis_Processing"));

                result.Success = true;
                result.Report = response;
                result.Actions = ParseActionButtons(response);
                LogAnalysis(requestId, $"actions parsed - count={result.Actions?.Count ?? 0}");

                // Phase 5: 100% - Complete
                progressCallback?.Invoke(100, LocalizationService.Get("Analysis_Complete"));
            }
            catch (OperationCanceledException)
            {
                LogAnalysis(requestId, "cancelled by user");
                result.Success = false;
                result.ErrorMessage = "";
            }
            catch (Exception ex)
            {
                LogAnalysis(requestId, $"exception: {ex.Message}");
                Logger.Log("AnalysisService", $"[GRAPH_ANALYSIS] rid={requestId} StackTrace: {ex.StackTrace}");
                result.Success = false;
                result.ErrorMessage = LocalizationService.Format("Analysis_ErrorOccurred", ex.Message);
            }

            LogAnalysis(requestId, $"final result - success={result.Success}");
            return result;
        }

        /// <summary>
        /// Build a compact text for RAG keyword extraction from the graph's Python code and errors.
        /// </summary>
        private static string BuildRagQueryText(GraphAnalysisData graphData)
        {
            var sb = new StringBuilder();
            sb.Append("Dynamo graph Revit API analysis. ");

            if (graphData.Nodes != null)
            {
                foreach (var node in graphData.Nodes)
                {
                    if (!string.IsNullOrEmpty(node.ErrorMessage))
                        sb.Append($"Error: {node.ErrorMessage}. ");

                    if (!string.IsNullOrEmpty(node.PythonCode))
                    {
                        string snippet = node.PythonCode.Length > 500
                            ? node.PythonCode.Substring(0, 500)
                            : node.PythonCode;
                        sb.Append(snippet);
                        sb.Append(' ');
                    }
                }
            }

            string query = sb.ToString();
            return query.Length > 3000 ? query.Substring(0, 3000) : query;
        }
        
        /// <summary>
        /// Serialize graph data to JSON
        /// </summary>
        private static string SerializeGraphData(GraphAnalysisData data)
        {
            // Compact JSON — the LLM does not benefit from indentation, and large graphs
            // bloat ~30% with whitespace.
#if NET48
            return Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.None,
                new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
#else
            return JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
#endif
        }

        private static string BuildAnalysisPrompt(string graphJson, string revitVersion, string dynamoVersion)
        {
            // Determine Python engine based on Revit version
            string pythonEngine = revitVersion == "2022" ? "IronPython 2.7" : "CPython 3.x";
            
            if (AppLanguage.IsEnglish)
            {
                return $@"
# Role
You are an expert Dynamo graph analyzer for **Revit {revitVersion} + Dynamo {dynamoVersion}** using **{pythonEngine}**.

# CRITICAL - Environment Context
- **Revit Version**: {revitVersion}
- **Dynamo Version**: {dynamoVersion}  
- **Python Engine**: {pythonEngine}
- Verify API compatibility for Revit {revitVersion} ONLY (use the pre-fetched API documentation provided)
- Do NOT suggest APIs from other Revit versions

# Graph Data (JSON)
The JSON includes:
- `environment`: Version info (use this for API verification)
- `nodes`: Each node with state, pythonCode, codeBlockContent, inputValue, lacingMode
- `wires`: Connections between nodes
- `disconnectedInputs`: Ports that need connections
- `groups`: Existing annotations

```json
{graphJson}
```

# Analysis Requirements
Analyze the graph and provide a report in the following EXACT format (in English).
**PRIORITY ORDER**: Report issues in this order of severity.

## Context
[Summarize the overall intent and purpose of this graph in 2-3 sentences]

## Diagnosis

### 🔴 Critical Errors - HIGHEST PRIORITY
[List nodes with Error state. For each error:]
- Node ID and Name
- Exact error message
- If Python node: analyze the code and identify the specific line/API causing the error

### 🟡 Warnings - MEDIUM PRIORITY
[List nodes with Warning state or potential issues:]
- Deprecated API usage (verify for Revit {revitVersion})
- Type mismatches in connections
- Potential null reference issues

### ⚪ Incomplete Elements - LOW PRIORITY
[List disconnected required input ports that need connections]

## Python Code Review
[For EACH Python node in the graph:]
1. **Node**: [Node Name] (ID: xxx)
2. **Code Analysis**:
   - API Compatibility: [Check if all APIs exist in Revit {revitVersion}]
   - Syntax Issues: [Check for {pythonEngine} compatibility]
   - Logic Issues: [Null checks, transaction handling, etc.]
3. **Specific Fixes**: [Line-by-line fixes if needed]

## Solutions
[For each issue identified above, provide a specific solution with detailed step-by-step instructions]

## Optimization
[Suggest improvements for efficiency, better node alternatives, or missing finishing touches]

# CRITICAL RULES
1. **Version-Specific Analysis**: Only use Revit {revitVersion} APIs — use the pre-fetched API documentation for verification
2. **Python Engine Awareness**:
   - Revit 2022 uses IronPython 2.7 (no f-strings, no type hints)
   - Revit 2023+ uses CPython 3.x
3. **Analyze Actual Code**: If pythonCode or codeBlockContent exists, analyze it thoroughly
4. **Check Input Values**: Verify inputValue fields for Number/String nodes
5. **Lacing Awareness**: Consider lacingMode when analyzing list operations
6. **Only Report ACTUAL Issues**: Do not suggest hypothetical problems
7. **Be Specific**: Reference actual node names, IDs, and line numbers
8. **All text must be in English** except code snippets
9. **Warning Nodes with Error Messages**: If a node has state=""Warning"" AND its errorMessage contains runtime error text (TypeError, ValueError, AttributeError, KeyError, etc.), treat it as a CRITICAL error and report it under 🔴, not 🟡. The Warning state in Dynamo often represents runtime execution failures.

# Time Estimate
At the end, provide: ""Estimated fix time: [X minutes]"" based on issue complexity.
";
            }

            return $@"
# Role
You are an expert Dynamo graph analyzer for **Revit {revitVersion} + Dynamo {dynamoVersion}** using **{pythonEngine}**.

# CRITICAL - Environment Context
- **Revit Version**: {revitVersion}
- **Dynamo Version**: {dynamoVersion}  
- **Python Engine**: {pythonEngine}
- Verify API compatibility for Revit {revitVersion} ONLY (use the pre-fetched API documentation provided)
- Do NOT suggest APIs from other Revit versions

# Graph Data (JSON)
The JSON includes:
- `environment`: Version info (use this for API verification)
- `nodes`: Each node with state, pythonCode, codeBlockContent, inputValue, lacingMode
- `wires`: Connections between nodes
- `disconnectedInputs`: Ports that need connections
- `groups`: Existing annotations

```json
{graphJson}
```

# Analysis Requirements
Analyze the graph and provide a report in the following EXACT format (in Korean).
**PRIORITY ORDER**: Report issues in this order of severity.

## 맥락 파악 (Context)
[Summarize the overall intent and purpose of this graph in 2-3 sentences]

## 문제 진단 (Diagnosis)

### 🔴 심각한 오류 (Critical Errors) - HIGHEST PRIORITY
[List nodes with Error state. For each error:]
- Node ID and Name
- Exact error message
- If Python node: analyze the code and identify the specific line/API causing the error

### 🟡 경고 및 잠재적 위험 (Warnings) - MEDIUM PRIORITY
[List nodes with Warning state or potential issues:]
- Deprecated API usage (verify for Revit {revitVersion})
- Type mismatches in connections
- Potential null reference issues

### ⚪ 미완성 요소 (Incomplete) - LOW PRIORITY
[List disconnected required input ports that need connections]

## Python 코드 검증 (Python Code Review)
[For EACH Python node in the graph:]
1. **Node**: [Node Name] (ID: xxx)
2. **Code Analysis**:
   - API Compatibility: [Check if all APIs exist in Revit {revitVersion}]
   - Syntax Issues: [Check for {pythonEngine} compatibility]
   - Logic Issues: [Null checks, transaction handling, etc.]
3. **Specific Fixes**: [Line-by-line fixes if needed]

## 해결 솔루션 (Solutions)
[For each issue identified above, provide a specific solution with detailed step-by-step instructions]

## 최적화 및 마무리 (Optimization)
[Suggest improvements for efficiency, better node alternatives, or missing finishing touches]

# CRITICAL RULES
1. **Version-Specific Analysis**: Only use Revit {revitVersion} APIs — use the pre-fetched API documentation for verification
2. **Python Engine Awareness**:
   - Revit 2022 uses IronPython 2.7 (no f-strings, no type hints)
   - Revit 2023+ uses CPython 3.x
3. **Analyze Actual Code**: If pythonCode or codeBlockContent exists, analyze it thoroughly
4. **Check Input Values**: Verify inputValue fields for Number/String nodes
5. **Lacing Awareness**: Consider lacingMode when analyzing list operations
6. **Only Report ACTUAL Issues**: Do not suggest hypothetical problems
7. **Be Specific**: Reference actual node names, IDs, and line numbers
8. **All text must be in Korean** except code snippets
9. **Warning Nodes with Error Messages**: If a node has state=""Warning"" AND its errorMessage contains runtime error text (TypeError, ValueError, AttributeError, KeyError, etc.), treat it as a CRITICAL error and report it under 🔴, not 🟡. The Warning state in Dynamo often represents runtime execution failures.

# Time Estimate
At the end, provide: ""예상 수정 시간: [X분]"" based on issue complexity.
";
        }


        /// <summary>
        /// Parse ACTION markers from the report to create interactive buttons
        /// Format: [ACTION:TYPE|param1=value1|param2=value2]
        /// </summary>
        private static List<AnalysisAction> ParseActionButtons(string report)
        {
            var actions = new List<AnalysisAction>();
            
            if (string.IsNullOrEmpty(report))
                return actions;

            try
            {
                // Look for ACTION markers in the format: [ACTION:TYPE|param1=value1|param2=value2]
                var matches = _actionButtonRegex.Matches(report);

                int actionIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string actionTypeStr = match.Groups[1].Value;
                        string paramsStr = match.Groups[2].Value;

                        var action = new AnalysisAction
                        {
                            ActionId = $"action_{actionIndex++}"
                        };

                        // Parse action type
                        if (Enum.TryParse<ActionType>(actionTypeStr, out var actionType))
                        {
                            action.Type = actionType;
                        }
                        else
                        {
                            continue; // Skip unknown action types
                        }

                        // Parse parameters
                        var paramPairs = paramsStr.Split('|');
                        foreach (var pair in paramPairs)
                        {
                            var keyValue = pair.Split(new[] { '=' }, 2);
                            if (keyValue.Length == 2)
                            {
                                string key = keyValue[0].Trim().ToLower();
                                string value = keyValue[1].Trim();

                                switch (key)
                                {
                                    case "target_id":
                                        action.TargetNodeId = value;
                                        break;
                                    case "target_name":
                                        action.TargetNodeName = value;
                                        break;
                                    case "target_port":
                                        if (int.TryParse(value, out int targetPort))
                                            action.TargetPortIndex = targetPort;
                                        break;
                                    case "source_id":
                                        action.SourceNodeId = value;
                                        break;
                                    case "source_name":
                                        action.SourceNodeName = value;
                                        break;
                                    case "source_port":
                                        if (int.TryParse(value, out int sourcePort))
                                            action.SourcePortIndex = sourcePort;
                                        break;
                                    case "node_type":
                                        action.NodeTypeToAdd = value;
                                        break;
                                    case "x":
                                        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x))
                                            action.SuggestedX = x;
                                        break;
                                    case "y":
                                        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y))
                                            action.SuggestedY = y;
                                        break;
                                    case "old_value":
                                        action.OldValue = value;
                                        break;
                                    case "new_value":
                                        action.NewValue = value;
                                        break;
                                    case "lacing":
                                        action.LacingMode = value;
                                        break;
                                    case "node_ids":
                                        action.NodeIds = new List<string>(value.Split(','));
                                        break;
                                    case "group_title":
                                        action.GroupTitle = value;
                                        break;
                                    case "group_color":
                                        action.GroupColor = value;
                                        break;
                                    case "note_text":
                                        action.NoteText = value;
                                        break;
                                    case "display":
                                        action.DisplayText = value;
                                        break;
                                    case "desc":
                                        action.Description = value;
                                        break;
                                }
                            }
                        }

                        // Set default display text if not provided
                        if (string.IsNullOrEmpty(action.DisplayText))
                        {
                            action.DisplayText = GetDefaultDisplayText(action.Type);
                        }

                        actions.Add(action);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BIBIM-Analysis] ParseActionButtons error: {ex.Message}");
            }

            return actions;
        }

        /// <summary>
        /// Get default display text for action type
        /// </summary>
        private static string GetDefaultDisplayText(ActionType type)
        {
            switch (type)
            {
                case ActionType.ADD_NODE: return LocalizationService.Get("Analysis_DisplayText_AddNode");
                case ActionType.DELETE_NODE: return LocalizationService.Get("Analysis_DisplayText_DeleteNode");
                case ActionType.REPLACE_NODE: return LocalizationService.Get("Analysis_DisplayText_ReplaceNode");
                case ActionType.CONNECT: return LocalizationService.Get("Analysis_DisplayText_Connect");
                case ActionType.DISCONNECT: return LocalizationService.Get("Analysis_DisplayText_Disconnect");
                case ActionType.RECONNECT: return LocalizationService.Get("Analysis_DisplayText_Reconnect");
                case ActionType.FIX_CODE: return LocalizationService.Get("Analysis_DisplayText_FixCode");
                case ActionType.REPLACE_CODE: return LocalizationService.Get("Analysis_DisplayText_ReplaceCode");
                case ActionType.SET_VALUE: return LocalizationService.Get("Analysis_DisplayText_SetValue");
                case ActionType.SET_LACING: return LocalizationService.Get("Analysis_DisplayText_SetLacing");
                case ActionType.GROUP_NODES: return LocalizationService.Get("Analysis_DisplayText_GroupNodes");
                case ActionType.ADD_NOTE: return LocalizationService.Get("Analysis_DisplayText_AddNote");
                default: return LocalizationService.Get("Analysis_DisplayText_Execute");
            }
        }

    }

    #region Analysis Result Models

    public class AnalysisResult
    {
        public bool Success { get; set; }
        public string Report { get; set; }
        public string ErrorMessage { get; set; }
        public List<AnalysisAction> Actions { get; set; } = new List<AnalysisAction>();
    }

    /// <summary>
    /// 12 Action Types for Auto-Fix System
    /// </summary>
    public enum ActionType
    {
        ADD_NODE,       // 노드 생성 + 연결
        DELETE_NODE,    // 노드 삭제
        REPLACE_NODE,   // 노드 교체
        CONNECT,        // 두 노드 연결
        DISCONNECT,     // 연결 해제
        RECONNECT,      // 연결 대상 변경
        FIX_CODE,       // Python 코드 수정
        REPLACE_CODE,   // Python 코드 전체 교체
        SET_VALUE,      // Code Block/Number 값 변경
        SET_LACING,     // Lacing 설정 변경
        GROUP_NODES,    // 노드 그룹화
        ADD_NOTE        // 설명 노트 추가
    }

    public class AnalysisAction
    {
        public ActionType Type { get; set; }
        public string ActionId { get; set; } // Unique ID for button reference
        public string DisplayText { get; set; } // Button label
        public string Description { get; set; } // What this action does
        
        // Target identification
        public string TargetNodeId { get; set; }
        public string TargetNodeName { get; set; }
        public int TargetPortIndex { get; set; } = -1;
        
        // Source for connections
        public string SourceNodeId { get; set; }
        public string SourceNodeName { get; set; }
        public int SourcePortIndex { get; set; } = -1;
        
        // For ADD_NODE, REPLACE_NODE
        public string NodeTypeToAdd { get; set; }
        public double SuggestedX { get; set; }
        public double SuggestedY { get; set; }
        
        // For FIX_CODE, REPLACE_CODE, SET_VALUE
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        
        // For SET_LACING
        public string LacingMode { get; set; } // Auto, Shortest, Longest, CrossProduct
        
        // For GROUP_NODES
        public List<string> NodeIds { get; set; }
        public string GroupTitle { get; set; }
        public string GroupColor { get; set; }
        
        // For ADD_NOTE
        public string NoteText { get; set; }
        public double NoteX { get; set; }
        public double NoteY { get; set; }
    }

    #endregion
}
