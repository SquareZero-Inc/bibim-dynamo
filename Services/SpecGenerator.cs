// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if NET48
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#else
using System.Text.Json;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Generates and revises code specifications using Claude API.
    /// This service creates structured specifications from user requests,
    /// allowing users to review and confirm before code generation.
    /// </summary>
    /// <remarks>
    /// Requirements:
    /// - 1.1: WHEN a user submits a code generation request, THE Spec_Generator SHALL analyze the request and produce a structured specification
    /// - 1.3: WHEN the request is ambiguous, THE Spec_Generator SHALL include specific clarifying questions in the specification
    /// </remarks>
    public static class SpecGenerator
    {
        private const string ClaudeApiUrl = "https://api.anthropic.com/v1/messages";

        private static void LogPerf(string requestId, string step, long elapsedMs, string detail = null)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return;
            string suffix = string.IsNullOrWhiteSpace(detail) ? "" : $" detail={detail}";
            Logger.Log("SpecGenerator", $"[PERF] rid={requestId} step={step} ms={elapsedMs}{suffix}");
        }

        /// <summary>
        /// Response format prefix for specification responses.
        /// </summary>
        public const string SpecResponsePrefix = "TYPE: SPEC|";

        /// <summary>
        /// Response format prefix for general chat responses (non-code requests).
        /// </summary>
        public const string ChatResponsePrefix = "TYPE: CHAT|";

        /// <summary>
        /// Gets the system prompt for specification generation.
        /// The prompt instructs the AI to analyze user requests and produce
        /// structured specifications in Korean.
        /// </summary>
        /// <param name="revitVersion">The target Revit version for context.</param>
        /// <returns>The system prompt string for specification generation.</returns>
        public static string GetSpecificationPrompt(string revitVersion)
        {
            if (AppLanguage.IsEnglish)
            {
                return $@"
# Role
You are the 'BIBIM Specification Agent'. Analyze user messages and either:
1. Generate a structured specification for Dynamo Python Script requests
2. Respond conversationally for general chat

[CONTEXT]
- Expert in Revit API version {revitVersion}
- Target environment: Revit {revitVersion} + Dynamo
- BIBIM AI has two core features:
  1) Code Generation
  2) Graph Analysis (via the Analysis button)
- BIBIM can automatically create/place a Python Script node on the Dynamo canvas and inject generated code.
- Never claim that node creation/placement is impossible for code-generation requests.

[MANDATORY CLASSIFICATION]
Classify first:
- ERROR_REPORT: user reports runtime/code errors
- GENERAL_CHAT: greeting, thanks, capability questions, non-code conversation
- CODE_REQUEST: user wants script/automation/code behavior
- Requests like ""create/add/place a new Python node"" are CODE_REQUEST.

[RESPONSE RULES]
- ERROR_REPORT => TYPE: CHAT|Explain likely cause and fix direction in English.
- GENERAL_CHAT => TYPE: CHAT|Friendly, concise English response.
- CODE_REQUEST => TYPE: SPEC|JSON only.

[FOR CODE_REQUEST]
- Never assume missing details.
- Ask clarifying questions with concrete options.
- If request is ambiguous or contradictory, include questions.
- If clear, set questions to [].

[OUTPUT FORMAT]
For chat:
TYPE: CHAT|response text

For spec:
TYPE: SPEC|
{{
  ""inputs"": [
    {{""name"": ""Input name"", ""type"": ""Element type"", ""description"": ""Description""}}
  ],
  ""steps"": [
    ""Step 1"",
    ""Step 2""
  ],
  ""output"": {{""type"": ""Output type"", ""description"": ""Output description"", ""unit"": ""Unit if any""}},
  ""questions"": [
    ""Clarifying question with options""
  ]
}}

[GLOBAL RULES — ABSOLUTE]
1. EVERY response MUST begin with exactly ""TYPE: CHAT|"" or ""TYPE: SPEC|"". No exceptions. No preamble. No markdown. The very first characters of your output MUST be one of these two prefixes.
2. Write all response text in English (JSON keys remain English).
3. Never include Python code in a specification.
4. Never include markdown code blocks.
5. If user asks about graph/nodes/errors/capabilities, mention the Analysis button.
6. For node-creation requests, explain that BIBIM can auto-create/place Python Script nodes after code generation.
7. If you are unsure whether the request is CODE_REQUEST or GENERAL_CHAT, default to TYPE: CHAT| with a helpful response. NEVER output bare text without a prefix.
8. VIOLATION: Any response that does NOT start with ""TYPE: CHAT|"" or ""TYPE: SPEC|"" is a protocol violation and will cause a system error.
9. NEVER use Revit API call syntax (e.g., ElementId.Value, Document.Delete, Wall.Location) in specification steps. Use natural language descriptions instead (e.g., ""record each wall's ID value"" instead of ""record with ElementId.Value""). API call forms in steps cause downstream code generation to use them as static calls, triggering validation blocks.";
            }

            return $@"
# Role
You are the 'BIBIM Specification Agent'. Your goal is to analyze user messages and either:
1. Generate a structured specification for Dynamo Python Script requests
2. Respond conversationally for general chat messages

[CONTEXT]
- You are an expert in Revit API for version {revitVersion}
- Target: **Revit {revitVersion} + Dynamo**
- You handle BOTH code generation requests AND general conversation
- You are part of BIBIM AI, a Dynamo View Extension with TWO core features:
  1. **코드 생성 (Generation Mode)**: 사용자 요청을 분석하여 Dynamo Python Script 코드를 생성
  2. **그래프 분석 (Analysis Mode)**: 채팅창 상단의 '노드 그래프 분석' 버튼을 클릭하면, 현재 워크스페이스의 모든 노드를 AI가 자동으로 읽고 진단 (노드 시스템 해석, 오류 감지, 최적화 제안)
- IMPORTANT: 사용자가 노드 구성, 그래프 상태, 오류 진단 등에 대해 물어보면, 반드시 '분석 기능'을 안내해야 합니다. 분석 버튼을 누르면 AI가 워크스페이스의 모든 노드를 자동으로 읽고 분석할 수 있다고 알려주세요.
- IMPORTANT: BIBIM은 코드 생성 후 Python Script 노드를 캔버스에 자동 생성/배치하고 코드를 자동 주입할 수 있습니다.
- 절대 ""직접 노드를 생성/배치할 수 없다""라고 안내하지 마세요(코드 생성 요청 기준).

---

[STEP 1: MESSAGE CLASSIFICATION - MANDATORY]
FIRST, classify the user's message into one of these categories:

**CODE_REQUEST** - User wants code/script/automation:
- Keywords: 만들어, 생성해, 계산해, 추출해, 변경해, 수정해, 삭제해, 필터링해, 스크립트, 코드, 자동화
- Examples: ""벽 면적 계산해줘"", ""파라미터 값 변경해줘"", ""선택한 요소 삭제해줘""
- Includes: ""새 노드 만들어줘"", ""Python 노드 추가해줘"", ""노드 배치해줘""

**ERROR_REPORT** - User is reporting an error from previous code execution (MUST CHECK BEFORE CODE_REQUEST):
- Contains Python error keywords: SyntaxError, TypeError, IndentationError, NameError, AttributeError, ImportError, ValueError, KeyError, IndexError, RuntimeError, Exception
- Contains Dynamo error patterns: ""PythonEvaluator.Evaluate 작업이 실패했습니다"", ""Warning:"", ""Error:"", ""unexpected EOF""
- Contains error-related Korean: ""오류가 발생"", ""에러가 나"", ""실행이 안"", ""작동이 안"", ""안 돼"", ""실패"", ""코드가 잘렸""
- User is pasting a traceback or error message from Dynamo execution
- IMPORTANT: If the message contains error keywords/patterns, classify as ERROR_REPORT even if it also looks like a code request

**GENERAL_CHAT** - NOT a code request:
- Greetings: 안녕, 하이, 반가워
- Thanks/Confirmation: 고마워, 감사, 오케이, 굳, 좋아, 알겠어, 확인
- Questions about capabilities: 뭐 할 수 있어?, 도움말
- Questions about graph/nodes: 노드 보여?, 그래프 분석해줘, 오류 찾아줘, 내 노드 확인해줘
- Feedback on previous response: 잘 됐어, 완벽해, 좋네
- Any message that is NOT asking for code generation
- Do NOT classify node-creation requests as GENERAL_CHAT

---

[STEP 2: RESPOND BASED ON CLASSIFICATION]

**If ERROR_REPORT → Use TYPE: CHAT format:**
TYPE: CHAT|이전 코드 실행에서 오류가 발생한 것 같습니다.

[오류 내용을 분석하여 원인과 해결 방향을 한국어로 설명. 이전 대화 맥락이 있다면 참고하여 구체적으로 진단.]

수정된 코드를 바로 생성해드릴까요? 아니면 추가 정보가 필요하시면 말씀해주세요.
채팅창 하단의 **분석 버튼**을 누르시면 현재 그래프 상태를 AI가 자동으로 진단할 수도 있습니다.

IMPORTANT for ERROR_REPORT:
- Do NOT generate a specification card for error messages
- Analyze the error and explain the likely cause in Korean
- If you can identify the fix from conversation context, offer to generate corrected code
- If the error mentions ""복사"" or copy issues, guide user to right-click → ""복사"" (NEVER suggest Ctrl+C)

**If GENERAL_CHAT → Use TYPE: CHAT format:**
TYPE: CHAT|친근하고 도움이 되는 한국어 응답

**If CODE_REQUEST → Use TYPE: SPEC format (see below)**

---

[CRITICAL RULES FOR CODE_REQUEST]
1. NEVER guess or assume. If uncertain about ANY detail, ASK.
2. One wrong assumption is worse than ten clarifying questions.
3. Ask ALL necessary questions at once - don't split across multiple turns.
4. Detect logical contradictions in the request and point them out.
5. Each question MUST provide concrete options when possible.

---

[COMPLETE REQUEST CRITERIA - Internal Check]
A request is complete ONLY when ALL of these are clear:
- Target element type is explicitly specified
- Operation scope is defined (selected/view/document)
- Input/output format is clear
- No logical contradictions exist
- No ambiguous expressions remain

If ANY criteria fails → You MUST ask clarifying questions before proceeding.

---

[AMBIGUITY CHECKLIST - MANDATORY]
Before generating the specification, check ALL of these categories:

**[Target Related]**
1. Element type unclear (Wall, Floor, Door, Window, Room, FamilyInstance, Ceiling, Roof, Stair, Railing, etc.)
2. Element scope unclear (Selected only / Current view / Active document / Include linked models)
3. Filter conditions unclear (Specific level? Phase? Workset? Parameter-based filter?)

**[Parameter Related]**
4. Parameter name missing or unclear
5. Instance vs Type parameter not specified
6. Parameter data type unclear (Text, Number, Yes/No, ElementId, Length, Area)
7. Unit unclear (mm, m, ft, inch, sqm, sqft)

**[Operation Related]**
8. Operation direction unclear (Read / Write / Both)
9. Value to set is missing or unclear
10. Calculation method unclear (Sum, Average, Count, Min, Max, List all)
11. Error handling unclear (Skip errors? Stop on error? Log and continue?)

**[Output Related]**
12. Output format unclear (Single value / List / Nested list / Dictionary)
13. Output destination unclear (OUT variable / Write to parameter / Export to file)

**[Logic Related]**
14. Logical contradiction detected (e.g., ""delete all walls and calculate wall area"")
15. Ambiguous expressions requiring clarification (e.g., ""some"", ""a few"", ""nearby"")

---

[QUESTION FORMAT - MANDATORY]
When asking questions, ALWAYS provide concrete options:

❌ BAD: ""어떤 요소를 대상으로 하시겠습니까?""
✅ GOOD: ""대상 요소 타입이 무엇인가요? (벽 / 바닥 / 문 / 창문 / 룸 / 기타: ___)""

❌ BAD: ""범위가 어떻게 되나요?""
✅ GOOD: ""작업 범위를 선택해주세요: (선택된 요소만 / 현재 뷰의 모든 요소 / 문서 전체)""

❌ BAD: ""파라미터 정보가 필요합니다""
✅ GOOD: ""어떤 파라미터를 사용하시겠습니까? (파라미터 이름을 정확히 입력해주세요. 예: 'Mark', 'Comments', 'Area')""

---

[OUTPUT FORMAT - MANDATORY]

**For GENERAL_CHAT messages:**
TYPE: CHAT|응답 텍스트

**For CODE_REQUEST messages:**
TYPE: SPEC|
{{
  ""inputs"": [
    {{""name"": ""입력 이름"", ""type"": ""요소 타입"", ""description"": ""설명""}}
  ],
  ""steps"": [
    ""처리 단계 1"",
    ""처리 단계 2""
  ],
  ""output"": {{""type"": ""출력 타입"", ""description"": ""출력 설명"", ""unit"": ""단위 (해당시)""}},
  ""questions"": [
    {{""question"": ""질문 텍스트"", ""options"": [""선택지1"", ""선택지2"", ""선택지3""]}},
    {{""question"": ""질문 텍스트2"", ""options"": [""선택지1"", ""선택지2""]}}
  ]
}}

IMPORTANT for questions:
- Each question MUST be an object with ""question"" (string) and ""options"" (array of strings).
- ""options"" provides 2–5 concrete choices. The UI will always show a free-text ""기타"" field, so do NOT include ""기타"" in the options array.
- If the request is perfectly clear, set ""questions"" to [].

[RULES — ABSOLUTE]
1. EVERY response MUST begin with exactly ""TYPE: CHAT|"" or ""TYPE: SPEC|"". No exceptions. No preamble. No markdown. The very first characters of your output MUST be one of these two prefixes.
2. Write all text content in Korean (except JSON keys)
3. For CHAT: Be friendly, helpful, and concise
4. For CHAT: When user asks about nodes, graph state, errors, or BIBIM capabilities, mention BOTH:
   - 코드 생성 시 Python Script 노드 자동 생성/배치 가능
   - 분석 버튼으로 기존 그래프 진단 가능
5. For SPEC: Include ALL clarifying questions at once if the request is ambiguous
6. For SPEC: If the request is perfectly clear, set ""questions"" to an empty array []
7. NEVER include any Python code in the specification
8. NEVER include markdown code blocks
9. Keep processing steps concise but clear
10. Be specific about input types (e.g., ""Wall elements"" not just ""elements"")
11. When detecting contradictions, explain the issue in a question
12. If you are unsure whether the request is CODE_REQUEST or GENERAL_CHAT, default to TYPE: CHAT| with a helpful response. NEVER output bare text without a prefix.
13. VIOLATION: Any response that does NOT start with ""TYPE: CHAT|"" or ""TYPE: SPEC|"" is a protocol violation and will cause a system error.
14. NEVER use Revit API call syntax (e.g., ElementId.Value, Document.Delete, Wall.Location) in specification steps. Use natural language instead (e.g., ""각 벽의 ID 값과 함께 기록"" instead of ""ElementId.Value와 함께 기록""). API 호출 형태가 steps에 포함되면 코드 생성 시 static 호출로 오인되어 검증 차단됩니다.

[EXAMPLE - General Chat (Greeting)]
User: ""안녕 뭐 할 수 있어?""

TYPE: CHAT|안녕하세요! 저는 Revit {revitVersion} + Dynamo용 BIBIM AI 에이전트입니다. 두 가지 핵심 기능으로 도와드릴 수 있습니다:

**[코드 생성]**
- 원하시는 작업을 말씀해주시면 Dynamo Python Script 코드를 생성해드립니다
- 벽, 바닥, 문, 창문, 룸 등 요소 관련 자동화
- 파라미터 읽기/쓰기, 일괄 변경
- 요소 정보 추출 (면적, 길이, 부피 등)

**[그래프 분석]**
- 채팅창 하단의 **분석 버튼**을 클릭하시면, 현재 워크스페이스의 모든 노드를 AI가 자동으로 읽고 진단해드립니다
- 노드 시스템 해석, 오류 감지, 최적화 제안까지 지원합니다

어떤 작업을 도와드릴까요?

[EXAMPLE - General Chat (Node/Graph Question)]
User: ""현재 내가 짜놓은 노드가 보여?""

TYPE: CHAT|현재 대화 텍스트만으로 기존 노드 상태를 직접 열람하진 못하지만, **분석 기능**을 사용하시면 워크스페이스의 모든 노드를 AI가 자동으로 읽고 진단해드릴 수 있습니다!

채팅창 하단의 **분석 버튼**을 클릭해보세요. 노드 구성, 연결 상태, 오류 등을 종합적으로 분석해드립니다. 😊

[EXAMPLE - Code Request (Node Creation)]
User: ""니가 새로 노드 만들어줘""

TYPE: SPEC|
{{
  ""inputs"": [
    {{""name"": ""대상 작업 설명"", ""type"": ""Text"", ""description"": ""노드에 넣을 로직 설명""}}
  ],
  ""steps"": [
    ""요청 로직에 맞는 Dynamo Python 코드 생성"",
    ""필요 입력(IN 포트) 개수 정의"",
    ""코드 주입 후 자동 생성/배치될 노드 기준으로 실행 가이드 제공""
  ],
  ""output"": {{""type"": ""Python Script node result"", ""description"": ""생성된 노드 실행 결과"", ""unit"": """"}},
  ""questions"": [
    {{""question"": ""어떤 로직의 노드를 원하시나요?"", ""options"": [""벽 면적 합계"", ""문 개수 집계"", ""파라미터 일괄 변경"", ""요소 필터링""]}}
  ]
}}

[EXAMPLE - General Chat (Confirmation/Thanks)]
User: ""오케이 굳""

TYPE: CHAT|감사합니다! 😊 추가로 필요하신 Dynamo Python 스크립트가 있으시면 언제든지 말씀해주세요.

[EXAMPLE - General Chat (Feedback)]
User: ""잘 됐어 고마워""

TYPE: CHAT|다행이네요! 코드가 잘 작동해서 기쁩니다. 다른 자동화 작업이 필요하시면 언제든 요청해주세요. 🚀

[EXAMPLE - Code Request (Clear)]
User: ""선택한 벽의 면적을 계산해줘""

TYPE: SPEC|
{{
  ""inputs"": [
    {{""name"": ""선택된 벽 요소"", ""type"": ""Wall elements"", ""description"": ""면적을 계산할 벽""}}
  ],
  ""steps"": [
    ""각 벽에서 Area 파라미터 추출"",
    ""단위 변환 (sq ft → ㎡)"",
    ""총합 계산""
  ],
  ""output"": {{""type"": ""Number"", ""description"": ""총 벽 면적"", ""unit"": ""㎡""}},
  ""questions"": []
}}

[EXAMPLE - Code Request (Ambiguous)]
User: ""파라미터 값을 변경해줘""

TYPE: SPEC|
{{
  ""inputs"": [
    {{""name"": ""대상 요소"", ""type"": ""Unknown"", ""description"": ""파라미터를 변경할 요소""}}
  ],
  ""steps"": [
    ""요소에서 파라미터 접근"",
    ""새 값으로 파라미터 설정""
  ],
  ""output"": {{""type"": ""Modified elements"", ""description"": ""수정된 요소"", ""unit"": """"}},
  ""questions"": [
    {{""question"": ""대상 요소 타입이 무엇인가요?"", ""options"": [""벽"", ""바닥"", ""문"", ""창문"", ""룸"", ""패밀리 인스턴스""]}},
    {{""question"": ""어떤 파라미터를 변경하시겠습니까? (정확한 파라미터 이름 입력)"", ""options"": [""Mark"", ""Comments"", ""사용자 정의 파라미터""]}},
    {{""question"": ""Instance 파라미터인가요, Type 파라미터인가요?"", ""options"": [""Instance"", ""Type""]}},
    {{""question"": ""작업 범위를 선택해주세요."", ""options"": [""선택된 요소만"", ""현재 뷰의 모든 요소"", ""문서 전체""]}}
  ]
}}

[EXAMPLE - Contradiction Detected]
User: ""모든 벽을 삭제하고 벽 면적 합계를 알려줘""

TYPE: SPEC|
{{
  ""inputs"": [
    {{""name"": ""대상 벽"", ""type"": ""Wall elements"", ""description"": ""삭제 및 면적 계산 대상""}}
  ],
  ""steps"": [
    ""(모순 감지됨)""
  ],
  ""output"": {{""type"": ""Unknown"", ""description"": ""요청 명확화 필요"", ""unit"": """"}},
  ""questions"": [
    {{""question"": ""요청에 모순이 있습니다: 벽을 삭제하면 면적을 계산할 수 없습니다. 어떤 작업을 원하시나요?"", ""options"": [""벽 삭제만"", ""면적 계산만"", ""면적 계산 후 삭제""]}}
  ]
}}
";
        }

        /// <summary>
        /// Generates a specification from a user request.
        /// </summary>
        /// <param name="userRequest">The user's code generation request.</param>
        /// <param name="history">Previous conversation history for context.</param>
        /// <returns>A structured CodeSpecification parsed from the AI response.</returns>
        /// <remarks>
        /// Requirement 1.1: Analyzes the request and produces a structured specification.
        /// Requirement 1.3: Includes clarifying questions when the request is ambiguous.
        /// </remarks>
        public static async Task<CodeSpecification> GenerateSpecificationAsync(
            string userRequest,
            IEnumerable<ChatMessage> history,
            string requestId = null,
            CancellationToken cancellationToken = default)
        {
            var totalSw = Stopwatch.StartNew();
            try
            {
                // Get configuration
                var config = ConfigService.GetRagConfig();
                string revitVersion = config.RevitVersion;
                string claudeModel = config.ClaudeModel;
                string claudeApiKey = GetClaudeApiKey();

                if (string.IsNullOrEmpty(claudeApiKey))
                {
                    throw new InvalidOperationException("Claude API key not configured");
                }

                // Build messages list from history
                var messagesList = new List<object>();

                if (history != null)
                {
                    foreach (var msg in history)
                    {
                        if (!ShouldIncludeMessageForSpecification(msg))
                        {
                            continue;
                        }

                        messagesList.Add(new
                        {
                            role = msg.IsUser ? "user" : "assistant",
                            content = msg.Text
                        });
                    }
                }

                // Add the current user request
                messagesList.Add(new
                {
                    role = "user",
                    content = userRequest
                });

                // Build request body
                var requestBody = new
                {
                    model = claudeModel,
                    max_tokens = 8192,
                    system = GetSpecificationPrompt(revitVersion),
                    messages = messagesList
                };

                var jsonContent = new StringContent(
#if NET48
                    JsonHelper.SerializeCamelCase(requestBody),
#else
                    JsonSerializer.Serialize(requestBody),
#endif
                    Encoding.UTF8,
                    "application/json");

                // Call Claude API
                using (var request = new HttpRequestMessage(HttpMethod.Post, ClaudeApiUrl))
                {
                    request.Content = jsonContent;
                    request.Headers.Add("x-api-key", claudeApiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");

                    var apiSw = Stopwatch.StartNew();
                    using (var response = await ClaudeApiClient._httpClient.SendAsync(request, cancellationToken))
                    {
                        apiSw.Stop();
                        LogPerf(requestId, "spec-api", apiSw.ElapsedMilliseconds, "generate");

                        if (!response.IsSuccessStatusCode)
                        {
                            var errorMsg = await response.Content.ReadAsStringAsync();
                            throw new HttpRequestException($"API Error: {response.StatusCode} - {errorMsg}");
                        }

                        var responseString = await response.Content.ReadAsStringAsync();
                        TrackClaudeTokenUsage(responseString, "spec_generate", claudeModel, requestId);
                        string aiResponse = ExtractTextFromClaudeResponse(responseString);

                        // Parse the specification from the response
                        var spec = ParseSpecificationResponse(aiResponse);
                        spec.OriginalRequest = userRequest;

                        Log($"GenerateSpecificationAsync: Generated spec {spec.SpecId} with {spec.ClarifyingQuestions.Count} questions");
                        return spec;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"GenerateSpecificationAsync Error: {ex.Message}");
                throw;
            }
            finally
            {
                totalSw.Stop();
                LogPerf(requestId, "spec", totalSw.ElapsedMilliseconds, "generate");
            }
        }

        /// <summary>
        /// Extracts text content from Claude API response.
        /// </summary>
        private static string ExtractTextFromClaudeResponse(string responseString)
        {
#if NET48
            var responseObj = JObject.Parse(responseString);
            var content = responseObj["content"];
            if (content != null && content.HasValues)
            {
                var textBuilder = new StringBuilder();
                foreach (var block in content)
                {
                    if (block["type"]?.ToString() == "text" && block["text"] != null)
                    {
                        textBuilder.Append(block["text"].ToString());
                    }
                }
                return textBuilder.ToString().Trim();
            }
            return string.Empty;
#else
            using (JsonDocument doc = JsonDocument.Parse(responseString))
            {
                if (doc.RootElement.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
                {
                    var textBuilder = new StringBuilder();
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var typeProp) &&
                            typeProp.GetString() == "text" &&
                            block.TryGetProperty("text", out var textProp))
                        {
                            textBuilder.Append(textProp.GetString());
                        }
                    }
                    return textBuilder.ToString().Trim();
                }
            }
            return string.Empty;
#endif
        }

        /// <summary>
        /// Parses the AI response into a structured CodeSpecification.
        /// Handles the TYPE: SPEC| response format and extracts JSON content.
        /// </summary>
        /// <param name="response">The raw AI response string.</param>
        /// <returns>A parsed CodeSpecification object.</returns>
        /// <remarks>
        /// If parsing fails, returns a safe chat-style fallback message.
        /// </remarks>
        public static CodeSpecification ParseSpecificationResponse(string response)
        {
            try
            {
                if (string.IsNullOrEmpty(response))
                {
                    return CreateFallbackSpec(response, "Empty response received");
                }

                // Check for TYPE: CHAT| prefix first (general conversation)
                if (response.Contains(ChatResponsePrefix))
                {
                    int prefixIndex = response.IndexOf(ChatResponsePrefix);
                    string chatText = response.Substring(prefixIndex + ChatResponsePrefix.Length).Trim();
                    
                    Log($"ParseSpecificationResponse: Detected CHAT response");
                    return new CodeSpecification
                    {
                        IsChatResponse = true,
                        ChatResponseText = chatText
                    };
                }

                if (ContainsProtocolViolationPrefix(response))
                {
                    Log("ParseSpecificationResponse: Protocol violation detected (non-spec payload)");
                    return CreateFallbackSpec(null, "Protocol violation: non-spec response in specification flow");
                }

                // Check for TYPE: SPEC| prefix
                string jsonContent;
                if (response.Contains(SpecResponsePrefix))
                {
                    int prefixIndex = response.IndexOf(SpecResponsePrefix);
                    jsonContent = response.Substring(prefixIndex + SpecResponsePrefix.Length).Trim();
                }
                else
                {
                    // Strict fallback: only parse if response itself starts with a JSON object.
                    string trimmedResponse = response.TrimStart();
                    if (trimmedResponse.StartsWith("{", StringComparison.Ordinal))
                    {
                        jsonContent = trimmedResponse;
                    }
                    else
                    {
                        // Graceful fallback: treat prefix-less non-JSON response as CHAT
                        // Claude sometimes responds without protocol prefix — show as normal text instead of error
                        Log("ParseSpecificationResponse: No prefix found, treating as CHAT response (graceful fallback)");
                        return new CodeSpecification
                        {
                            IsChatResponse = true,
                            ChatResponseText = response.Trim()
                        };
                    }
                }

                // Remove any trailing content after the JSON object
                jsonContent = ExtractJsonObject(jsonContent);

                // Parse the JSON into a temporary structure
                var parsedSpec = ParseSpecJson(jsonContent);

                // Create the CodeSpecification with parsed values
                var spec = new CodeSpecification
                {
                    SpecId = Guid.NewGuid().ToString(),
                    OriginalRequest = string.Empty, // Will be set by caller
                    Inputs = parsedSpec.Inputs ?? new List<SpecInput>(),
                    ProcessingSteps = parsedSpec.Steps ?? new List<string>(),
                    Output = parsedSpec.Output ?? new SpecOutput { Description = LocalizationService.Get("Spec_DefaultOutputDescription") },
                    ClarifyingQuestions = parsedSpec.Questions ?? new List<string>(),
                    ClarifyingQuestionsStructured = parsedSpec.QuestionsStructured ?? new List<ClarifyingQuestion>(),
                    RevisionNumber = 1,
                    CreatedAt = DateTime.UtcNow,
                    IsConfirmed = false
                };

                // Ensure non-null collections
                if (spec.Inputs == null) spec.Inputs = new List<SpecInput>();
                if (spec.ProcessingSteps == null) spec.ProcessingSteps = new List<string>();
                if (spec.Output == null) spec.Output = new SpecOutput { Description = LocalizationService.Get("Spec_DefaultOutputDescription") };
                if (spec.ClarifyingQuestions == null) spec.ClarifyingQuestions = new List<string>();
                if (spec.ClarifyingQuestionsStructured == null) spec.ClarifyingQuestionsStructured = new List<ClarifyingQuestion>();

                Log($"ParseSpecificationResponse: Parsed spec with {spec.Inputs.Count} inputs, {spec.ProcessingSteps.Count} steps, {spec.ClarifyingQuestions.Count} questions");
                return spec;
            }
            catch (Exception ex)
            {
                Logger.Log("SpecGenerator", $"[PARSE_ERROR] type={ex.GetType().Name} msg={ex.Message}");
                Log($"ParseSpecificationResponse Error: {ex.Message}");
                return CreateFallbackSpec(null, $"Parse error: {ex.Message}");
            }
        }

        private static bool ContainsProtocolViolationPrefix(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return false;
            }

            return response.IndexOf("TYPE: CODE|", StringComparison.OrdinalIgnoreCase) >= 0
                || response.IndexOf("TYPE: GUIDE|", StringComparison.OrdinalIgnoreCase) >= 0
                || response.IndexOf("TYPE: VALIDATION_BLOCK|", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldIncludeMessageForSpecification(ChatMessage msg)
        {
            if (msg == null || string.IsNullOrWhiteSpace(msg.Text))
            {
                return false;
            }

            return !ContainsProtocolViolationPrefix(msg.Text);
        }

        /// <summary>
        /// Extracts a complete JSON object from a string, handling nested braces.
        /// </summary>
        private static string ExtractJsonObject(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            int braceCount = 0;
            int startIndex = content.IndexOf('{');
            if (startIndex < 0)
                return content;

            for (int i = startIndex; i < content.Length; i++)
            {
                if (content[i] == '{')
                    braceCount++;
                else if (content[i] == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        return content.Substring(startIndex, i - startIndex + 1);
                    }
                }
            }

            // If we didn't find a complete object, return from start to end
            return content.Substring(startIndex);
        }

        /// <summary>
        /// Parses the JSON content into a temporary structure.
        /// </summary>
        private static ParsedSpecJson ParseSpecJson(string jsonContent)
        {
#if NET48
            var jsonObj = JObject.Parse(jsonContent);
            
            var result = new ParsedSpecJson
            {
                Inputs = new List<SpecInput>(),
                Steps = new List<string>(),
                Questions = new List<string>()
            };

            // Parse inputs
            var inputsArray = jsonObj["inputs"] as JArray;
            if (inputsArray != null)
            {
                foreach (var input in inputsArray)
                {
                    result.Inputs.Add(new SpecInput
                    {
                        Name = input["name"]?.ToString() ?? string.Empty,
                        Type = input["type"]?.ToString() ?? string.Empty,
                        Description = input["description"]?.ToString() ?? string.Empty
                    });
                }
            }

            // Parse steps
            var stepsArray = jsonObj["steps"] as JArray;
            if (stepsArray != null)
            {
                foreach (var step in stepsArray)
                {
                    result.Steps.Add(step.ToString());
                }
            }

            // Parse output
            var outputObj = jsonObj["output"] as JObject;
            if (outputObj != null)
            {
                result.Output = new SpecOutput
                {
                    Type = outputObj["type"]?.ToString() ?? string.Empty,
                    Description = outputObj["description"]?.ToString() ?? string.Empty,
                    Unit = outputObj["unit"]?.ToString() ?? string.Empty
                };
            }

            // Parse questions — supports both legacy string format and new object format
            result.QuestionsStructured = new List<ClarifyingQuestion>();
            var questionsArray = jsonObj["questions"] as JArray;
            if (questionsArray != null)
            {
                foreach (var question in questionsArray)
                {
                    if (question.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                    {
                        // New format: {"question": "...", "options": ["a", "b"]}
                        var qText = question["question"]?.ToString() ?? string.Empty;
                        var opts = new List<string>();
                        var optsArray = question["options"] as JArray;
                        if (optsArray != null)
                            foreach (var opt in optsArray)
                                opts.Add(opt.ToString());

                        result.QuestionsStructured.Add(new ClarifyingQuestion { Question = qText, Options = opts });
                        // Flat form: "질문 (선택지1 / 선택지2)"
                        result.Questions.Add(opts.Count > 0
                            ? qText + " (" + string.Join(" / ", opts) + ")"
                            : qText);
                    }
                    else
                    {
                        // Legacy format: plain string
                        var qText = question.ToString();
                        result.Questions.Add(qText);
                        result.QuestionsStructured.Add(new ClarifyingQuestion { Question = qText, Options = new List<string>() });
                    }
                }
            }

            return result;
#else
            using (JsonDocument doc = JsonDocument.Parse(jsonContent))
            {
                var result = new ParsedSpecJson
                {
                    Inputs = new List<SpecInput>(),
                    Steps = new List<string>(),
                    Questions = new List<string>()
                };

                var root = doc.RootElement;

                // Parse inputs
                if (root.TryGetProperty("inputs", out var inputsElement))
                {
                    foreach (var input in inputsElement.EnumerateArray())
                    {
                        result.Inputs.Add(new SpecInput
                        {
                            Name = input.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                            Type = input.TryGetProperty("type", out var type) ? type.GetString() ?? string.Empty : string.Empty,
                            Description = input.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty
                        });
                    }
                }

                // Parse steps
                if (root.TryGetProperty("steps", out var stepsElement))
                {
                    foreach (var step in stepsElement.EnumerateArray())
                    {
                        result.Steps.Add(step.GetString() ?? string.Empty);
                    }
                }

                // Parse output
                if (root.TryGetProperty("output", out var outputElement))
                {
                    result.Output = new SpecOutput
                    {
                        Type = outputElement.TryGetProperty("type", out var type) ? type.GetString() ?? string.Empty : string.Empty,
                        Description = outputElement.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
                        Unit = outputElement.TryGetProperty("unit", out var unit) ? unit.GetString() ?? string.Empty : string.Empty
                    };
                }

                // Parse questions — supports both legacy string format and new object format
                result.QuestionsStructured = new List<ClarifyingQuestion>();
                if (root.TryGetProperty("questions", out var questionsElement))
                {
                    foreach (var question in questionsElement.EnumerateArray())
                    {
                        if (question.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            // New format: {"question": "...", "options": ["a", "b"]}
                            var qText = question.TryGetProperty("question", out var qProp) ? qProp.GetString() ?? string.Empty : string.Empty;
                            var opts = new List<string>();
                            if (question.TryGetProperty("options", out var optsProp))
                                foreach (var opt in optsProp.EnumerateArray())
                                    opts.Add(opt.GetString() ?? string.Empty);

                            result.QuestionsStructured.Add(new ClarifyingQuestion { Question = qText, Options = opts });
                            result.Questions.Add(opts.Count > 0
                                ? qText + " (" + string.Join(" / ", opts) + ")"
                                : qText);
                        }
                        else
                        {
                            // Legacy format: plain string
                            var qText = question.GetString() ?? string.Empty;
                            result.Questions.Add(qText);
                            result.QuestionsStructured.Add(new ClarifyingQuestion { Question = qText, Options = new List<string>() });
                        }
                    }
                }

                return result;
            }
#endif
        }

        /// <summary>
        /// Temporary structure for parsing JSON response.
        /// </summary>
        private class ParsedSpecJson
        {
            public List<SpecInput> Inputs { get; set; }
            public List<string> Steps { get; set; }
            public SpecOutput Output { get; set; }
            /// <summary>Flat question strings for context/history recording.</summary>
            public List<string> Questions { get; set; }
            /// <summary>Structured questions with options for the interactive UI form.</summary>
            public List<ClarifyingQuestion> QuestionsStructured { get; set; }
        }

        /// <summary>
        /// Creates a fallback specification when parsing fails.
        /// Analyzes the error message to provide a user-friendly explanation.
        /// </summary>
        /// <param name="errorMessage">Description of the parsing error.</param>
        /// <returns>A minimal valid CodeSpecification.</returns>
        private static CodeSpecification CreateFallbackSpec(string _originalResponse, string errorMessage)
        {
            // Enhanced error logging for debugging "An error occurred" failures
            Logger.Log("SpecGenerator", $"[FALLBACK] error={errorMessage}");
            Logger.Log("SpecGenerator", $"[FALLBACK] response_length={_originalResponse?.Length ?? 0}");
            if (!string.IsNullOrEmpty(_originalResponse))
            {
                // Log first 300 chars of original response for diagnosis
                string preview = _originalResponse.Length > 300
                    ? _originalResponse.Substring(0, 300) + "...[truncated]"
                    : _originalResponse;
                Logger.Log("SpecGenerator", $"[FALLBACK] response_preview={preview}");
            }

            Log($"CreateFallbackSpec: {errorMessage}");

            string userMessage = ClassifyErrorMessage(errorMessage);

            return new CodeSpecification
            {
                IsChatResponse = true,
                ChatResponseText = userMessage
            };
        }

        /// <summary>
        /// Classifies an internal error message into a user-friendly localized string.
        /// </summary>
        private static string ClassifyErrorMessage(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
                return LocalizationService.Get("Spec_FallbackParseError");

            string lower = errorMessage.ToLowerInvariant();

            // JSON truncation — max_tokens exceeded or incomplete response
            if (lower.Contains("unterminated string") ||
                lower.Contains("unexpected end") ||
                lower.Contains("eof") ||
                lower.Contains("incomplete"))
            {
                return LocalizationService.Get("Spec_Error_ResponseTruncated");
            }

            // Empty or null response from API
            if (lower.Contains("empty response"))
            {
                return LocalizationService.Get("Spec_Error_EmptyResponse");
            }

            // Missing prefix or protocol violation — format issue
            if (lower.Contains("missing type:") ||
                lower.Contains("protocol violation") ||
                lower.Contains("prefix"))
            {
                return LocalizationService.Get("Spec_Error_InvalidFormat");
            }

            // Generic parse error
            if (lower.Contains("parse error"))
            {
                return LocalizationService.Get("Spec_Error_ParseFailed");
            }

            // Default fallback
            return LocalizationService.Get("Spec_FallbackParseError");
        }

        /// <summary>
        /// Gets the system prompt for specification revision.
        /// The prompt instructs the AI to revise an existing specification based on user feedback.
        /// </summary>
        /// <param name="revitVersion">The target Revit version for context.</param>
        /// <returns>The system prompt string for specification revision.</returns>
        public static string GetRevisionPrompt(string revitVersion)
        {
            if (AppLanguage.IsEnglish)
            {
                return $@"
# Role
You are the 'BIBIM Specification Agent'. Revise an existing specification based on user feedback, or respond conversationally when appropriate.

[CONTEXT]
- Expert in Revit API version {revitVersion}
- Target environment: Revit {revitVersion} + Dynamo
- User is revising an existing specification
- BIBIM can automatically create/place a Python Script node and inject generated code.
- Never claim that node creation/placement is impossible for code-generation or revision requests.

[MANDATORY CLASSIFICATION]
Classify feedback first:
- ERROR_REPORT: user reports runtime/code errors
- GENERAL_CHAT: non-revision chat
- REVISION_FEEDBACK: updates/answers to specification questions

[RESPONSE RULES]
- ERROR_REPORT => TYPE: CHAT|Explain likely cause and fix direction in English.
- GENERAL_CHAT => TYPE: CHAT|Friendly, concise English response.
- REVISION_FEEDBACK => TYPE: SPEC|Revised JSON specification.

[REVISION RULES]
- Apply user feedback to inputs/steps/output/questions.
- Remove answered questions.
- Keep unanswered questions.
- Add new questions when new ambiguity appears.
- Use concrete options in questions whenever possible.

[OUTPUT FORMAT]
For chat:
TYPE: CHAT|response text

For revised spec:
TYPE: SPEC|
{{
  ""inputs"": [
    {{""name"": ""Input name"", ""type"": ""Element type"", ""description"": ""Description""}}
  ],
  ""steps"": [
    ""Step 1"",
    ""Step 2""
  ],
  ""output"": {{""type"": ""Output type"", ""description"": ""Output description"", ""unit"": ""Unit if any""}},
  ""questions"": [
    {{""question"": ""Question text"", ""options"": [""Option 1"", ""Option 2"", ""Option 3""]}}
  ]
}}

IMPORTANT for questions:
- Each question MUST be an object with ""question"" (string) and ""options"" (array of strings).
- ""options"" provides 2–5 concrete choices. The UI always shows a free-text ""Other"" field, so do NOT include ""Other"" in options.
- If the request is perfectly clear, set ""questions"" to [].

[GLOBAL RULES — ABSOLUTE]
1. EVERY response MUST begin with exactly ""TYPE: CHAT|"" or ""TYPE: SPEC|"". No exceptions. No preamble. No markdown. The very first characters of your output MUST be one of these two prefixes.
2. Write all response text in English (JSON keys remain English).
3. Never include Python code in a specification.
4. Never include markdown code blocks.
5. If user asks about graph/nodes/errors/capabilities, mention the Analysis button.
6. For node-creation requests, explain that BIBIM can auto-create/place Python Script nodes after code generation.
7. If you are unsure whether the feedback is REVISION_FEEDBACK or GENERAL_CHAT, default to TYPE: CHAT| with a helpful response. NEVER output bare text without a prefix.
8. VIOLATION: Any response that does NOT start with ""TYPE: CHAT|"" or ""TYPE: SPEC|"" is a protocol violation and will cause a system error.
9. NEVER use Revit API call syntax (e.g., ElementId.Value, Document.Delete, Wall.Location) in specification steps. Use natural language descriptions instead (e.g., ""record each wall's ID value"" instead of ""record with ElementId.Value""). API call forms in steps cause downstream code generation to use them as static calls, triggering validation blocks.";
            }

            return $@"
# Role
You are the 'BIBIM Specification Agent'. Your goal is to revise an existing specification based on user feedback, or respond conversationally when appropriate.

[CONTEXT]
- You are an expert in Revit API for version {revitVersion}
- Target: **Revit {revitVersion} + Dynamo**
- You are REVISING an existing specification, not creating a new one
- The user has provided feedback/answers to previous questions
- You are part of BIBIM AI, a Dynamo View Extension with TWO core features:
  1. **코드 생성 (Generation Mode)**: 사용자 요청을 분석하여 Dynamo Python Script 코드를 생성
  2. **그래프 분석 (Analysis Mode)**: 채팅창 상단의 '노드 그래프 분석' 버튼을 클릭하면, 현재 워크스페이스의 모든 노드를 AI가 자동으로 읽고 진단 (노드 시스템 해석, 오류 감지, 최적화 제안)
- IMPORTANT: BIBIM은 코드 생성 후 Python Script 노드를 캔버스에 자동 생성/배치하고 코드를 자동 주입할 수 있습니다.
- 절대 ""직접 노드를 생성/배치할 수 없다""라고 안내하지 마세요(코드 생성/수정 요청 기준).

---

[STEP 1: FEEDBACK CLASSIFICATION - MANDATORY]
FIRST, classify the user's feedback into one of these categories:

**REVISION_FEEDBACK** - User is answering questions or requesting spec changes:
- Answering previous clarifying questions
- Requesting modifications to the specification
- Choosing between options (A/B/C)
- Providing additional details for the specification

**ERROR_REPORT** - User is reporting an error from previous code execution (MUST CHECK BEFORE REVISION_FEEDBACK):
- Contains Python error keywords: SyntaxError, TypeError, IndentationError, NameError, AttributeError, ImportError, ValueError, KeyError, IndexError, RuntimeError, Exception
- Contains Dynamo error patterns: ""PythonEvaluator.Evaluate 작업이 실패했습니다"", ""Warning:"", ""Error:"", ""unexpected EOF""
- Contains error-related Korean: ""오류가 발생"", ""에러가 나"", ""실행이 안"", ""작동이 안"", ""안 돼"", ""실패"", ""코드가 잘렸""
- User is pasting a traceback or error message from Dynamo execution
- IMPORTANT: If the message contains error keywords/patterns, classify as ERROR_REPORT even if it also looks like revision feedback

**GENERAL_CHAT** - NOT a revision or code request:
- The request cannot be automated with code (e.g., manual UI actions like package installation, file path selection, custom node editing)
- Greetings, thanks, confirmation
- Questions about capabilities or analysis features
- Feedback on previous response
- Node creation/placement requests are NOT GENERAL_CHAT (they are automatable via code-generation flow)

---

[STEP 2: RESPOND BASED ON CLASSIFICATION]

**If ERROR_REPORT → Use TYPE: CHAT format:**
TYPE: CHAT|이전 코드 실행에서 오류가 발생한 것 같습니다.

[오류 내용을 분석하여 원인과 해결 방향을 한국어로 설명. 이전 대화 맥락과 명세서를 참고하여 구체적으로 진단.]

수정된 코드를 바로 생성해드릴까요? 아니면 추가 정보가 필요하시면 말씀해주세요.
채팅창 하단의 **분석 버튼**을 누르시면 현재 그래프 상태를 AI가 자동으로 진단할 수도 있습니다.

IMPORTANT for ERROR_REPORT:
- Do NOT generate a specification card for error messages
- Analyze the error and explain the likely cause in Korean
- If you can identify the fix from conversation context, offer to generate corrected code
- If the error mentions ""복사"" or copy issues, guide user to right-click → ""복사"" (NEVER suggest Ctrl+C)

**If GENERAL_CHAT → Use TYPE: CHAT format:**
TYPE: CHAT|친근하고 도움이 되는 한국어 응답

IMPORTANT: If the user's request involves actions that CANNOT be automated with Python code (e.g., installing packages, selecting files in UI, editing custom node definitions manually), you MUST respond with TYPE: CHAT and explain why it cannot be automated, then suggest alternatives.
IMPORTANT: Requests to create/add/place Python Script nodes are automatable and must NOT be answered with ""cannot create/place nodes"".

**If REVISION_FEEDBACK → Use TYPE: SPEC format (see below)**

---

[CRITICAL RULES - NEVER SKIP]
1. NEVER guess or assume. If the user's answer is incomplete or creates new ambiguity, ASK.
2. If the user's feedback answers some questions but not all, keep the unanswered questions.
3. If the user's feedback creates NEW ambiguity, add NEW questions.
4. Detect logical contradictions and point them out.
5. Each question MUST provide concrete options when possible.

---

[YOUR TASK]
Revise the existing specification based on user feedback.
You will receive:
1. The original user request
2. The current specification (inputs, steps, output, questions)
3. User feedback/answers

Process the feedback:
- If feedback answers previous questions → Remove those questions, update spec accordingly
- If feedback is incomplete → Keep relevant questions
- If feedback creates new ambiguity → Add new questions with concrete options
- If feedback contains contradictions → Point them out in questions

---

[QUESTION FORMAT - MANDATORY]
When asking questions, ALWAYS provide concrete options:

❌ BAD: ""추가 정보가 필요합니다""
✅ GOOD: ""[구체적 항목]을 선택해주세요: (옵션1 / 옵션2 / 옵션3)""

---

[OUTPUT FORMAT - MANDATORY]

**For GENERAL_CHAT messages:**
TYPE: CHAT|응답 텍스트

**For REVISION_FEEDBACK messages:**
TYPE: SPEC|
{{
  ""inputs"": [
    {{""name"": ""입력 이름"", ""type"": ""요소 타입"", ""description"": ""설명""}}
  ],
  ""steps"": [
    ""처리 단계 1"",
    ""처리 단계 2""
  ],
  ""output"": {{""type"": ""출력 타입"", ""description"": ""출력 설명"", ""unit"": ""단위 (해당시)""}},
  ""questions"": [
    ""명확화 질문 1 (선택지 포함)"",
    ""명확화 질문 2 (선택지 포함)""
  ]
}}

[RULES — ABSOLUTE]
1. EVERY response MUST begin with exactly ""TYPE: CHAT|"" or ""TYPE: SPEC|"". No exceptions. No preamble. No markdown. The very first characters of your output MUST be one of these two prefixes.
2. Write all text content in Korean (except JSON keys)
3. For CHAT: Be friendly, helpful, and concise
4. For CHAT: When user asks about nodes, graph state, or BIBIM capabilities, mention BOTH:
   - 코드 생성 시 Python Script 노드 자동 생성/배치 가능
   - 분석 버튼으로 기존 그래프 진단 가능
5. Incorporate the user's feedback into the revised specification
6. If the feedback answers previous questions, remove those questions
7. If new ambiguities arise from the feedback, add new questions with options
8. If feedback is incomplete, keep unanswered questions
9. NEVER include any Python code in the specification
10. NEVER include markdown code blocks
11. Keep processing steps concise but clear
12. Preserve the intent of the original request while applying changes
13. When all ambiguities are resolved, set ""questions"" to empty array []
14. If you are unsure whether the feedback is REVISION_FEEDBACK or GENERAL_CHAT, default to TYPE: CHAT| with a helpful response. NEVER output bare text without a prefix.
15. VIOLATION: Any response that does NOT start with ""TYPE: CHAT|"" or ""TYPE: SPEC|"" is a protocol violation and will cause a system error.
16. NEVER use Revit API call syntax (e.g., ElementId.Value, Document.Delete, Wall.Location) in specification steps. Use natural language instead (e.g., ""각 벽의 ID 값과 함께 기록"" instead of ""ElementId.Value와 함께 기록""). API 호출 형태가 steps에 포함되면 코드 생성 시 static 호출로 오인되어 검증 차단됩니다.
";
        }

        /// <summary>
        /// Revises an existing specification based on user feedback.
        /// </summary>
        /// <param name="currentSpec">The current specification to revise.</param>
        /// <param name="userFeedback">The user's feedback describing desired changes.</param>
        /// <param name="conversationHistory">Previous conversation history for context.</param>
        /// <returns>A revised CodeSpecification with incremented revision number.</returns>
        /// <remarks>
        /// Requirement 3.3: Generates a revised specification incorporating the feedback.
        /// Requirement 5.1: Preserves the original request context.
        /// Property 7: OriginalRequest is preserved from the input spec.
        /// Property 8: RevisionNumber is incremented by 1.
        /// </remarks>
        public static async Task<CodeSpecification> ReviseSpecificationAsync(
            CodeSpecification currentSpec,
            string userFeedback,
            IEnumerable<ChatMessage> conversationHistory = null,
            string requestId = null,
            CancellationToken cancellationToken = default)
        {
            if (currentSpec == null)
            {
                throw new ArgumentNullException(nameof(currentSpec), "Current specification cannot be null");
            }

            if (string.IsNullOrEmpty(userFeedback))
            {
                throw new ArgumentException("User feedback cannot be null or empty", nameof(userFeedback));
            }

            var totalSw = Stopwatch.StartNew();
            try
            {
                // Get configuration
                var config = ConfigService.GetRagConfig();
                string revitVersion = config.RevitVersion;
                string claudeModel = config.ClaudeModel;
                string claudeApiKey = GetClaudeApiKey();

                if (string.IsNullOrEmpty(claudeApiKey))
                {
                    throw new InvalidOperationException("Claude API key not configured");
                }

                // Build the revision context message
                string currentSpecContext = BuildSpecificationContext(currentSpec);
                string revisionMessage = $@"[ORIGINAL REQUEST]
{currentSpec.OriginalRequest}

[CURRENT SPECIFICATION]
{currentSpecContext}

[USER FEEDBACK]
{userFeedback}

Please revise the specification based on the user's feedback.";

                // Build messages list with conversation history
                var messagesList = new List<object>();

                // Add conversation history for context
                if (conversationHistory != null)
                {
                    foreach (var msg in conversationHistory)
                    {
                        if (!ShouldIncludeMessageForSpecification(msg))
                        {
                            continue;
                        }

                        messagesList.Add(new
                        {
                            role = msg.IsUser ? "user" : "assistant",
                            content = msg.Text
                        });
                    }
                }

                // Add the revision message
                messagesList.Add(new
                {
                    role = "user",
                    content = revisionMessage
                });

                // Build request body
                var requestBody = new
                {
                    model = claudeModel,
                    max_tokens = 8192,
                    system = GetRevisionPrompt(revitVersion),
                    messages = messagesList
                };

                var jsonContent = new StringContent(
#if NET48
                    JsonHelper.SerializeCamelCase(requestBody),
#else
                    JsonSerializer.Serialize(requestBody),
#endif
                    Encoding.UTF8,
                    "application/json");

                // Call Claude API
                using (var request = new HttpRequestMessage(HttpMethod.Post, ClaudeApiUrl))
                {
                    request.Content = jsonContent;
                    request.Headers.Add("x-api-key", claudeApiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");

                    var apiSw = Stopwatch.StartNew();
                    var response = await ClaudeApiClient._httpClient.SendAsync(request, cancellationToken);
                    apiSw.Stop();
                    LogPerf(requestId, "spec-api", apiSw.ElapsedMilliseconds, "revise");

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorMsg = await response.Content.ReadAsStringAsync();
                        throw new HttpRequestException($"API Error: {response.StatusCode} - {errorMsg}");
                    }

                    var responseString = await response.Content.ReadAsStringAsync();
                    TrackClaudeTokenUsage(responseString, "spec_revise", claudeModel, requestId);
                    string aiResponse = ExtractTextFromClaudeResponse(responseString);

                    // Parse the specification from the response
                    var revisedSpec = ParseSpecificationResponse(aiResponse);

                    // CRITICAL: Preserve OriginalRequest from input spec (Property 7, Requirement 5.1)
                    revisedSpec.OriginalRequest = currentSpec.OriginalRequest;

                    // CRITICAL: Increment RevisionNumber (Property 8, Requirement 3.3)
                    revisedSpec.RevisionNumber = currentSpec.RevisionNumber + 1;

                    // Update timestamp for the revision
                    revisedSpec.CreatedAt = DateTime.UtcNow;

                    Log($"ReviseSpecificationAsync: Revised spec {revisedSpec.SpecId} to revision {revisedSpec.RevisionNumber}");
                    return revisedSpec;
                }
            }
            catch (Exception ex)
            {
                Log($"ReviseSpecificationAsync Error: {ex.Message}");
                throw;
            }
            finally
            {
                totalSw.Stop();
                LogPerf(requestId, "spec", totalSw.ElapsedMilliseconds, "revise");
            }
        }

        /// <summary>
        /// Builds a human-readable context string from a CodeSpecification.
        /// Used for providing context to the AI during revision.
        /// </summary>
        /// <param name="spec">The specification to convert to context.</param>
        /// <returns>A formatted string describing the specification.</returns>
        private static string BuildSpecificationContext(CodeSpecification spec)
        {
            var sb = new StringBuilder();

            // Inputs
            sb.AppendLine("Inputs:");
            if (spec.Inputs != null && spec.Inputs.Count > 0)
            {
                foreach (var input in spec.Inputs)
                {
                    sb.AppendLine($"  - {input.Name} ({input.Type}): {input.Description}");
                }
            }
            else
            {
                sb.AppendLine("  (none)");
            }

            // Processing steps
            sb.AppendLine("Processing Steps:");
            if (spec.ProcessingSteps != null && spec.ProcessingSteps.Count > 0)
            {
                for (int i = 0; i < spec.ProcessingSteps.Count; i++)
                {
                    sb.AppendLine($"  {i + 1}. {spec.ProcessingSteps[i]}");
                }
            }
            else
            {
                sb.AppendLine("  (none)");
            }

            // Output
            sb.AppendLine("Output:");
            if (spec.Output != null)
            {
                sb.AppendLine($"  Type: {spec.Output.Type}");
                sb.AppendLine($"  Description: {spec.Output.Description}");
                if (!string.IsNullOrEmpty(spec.Output.Unit))
                {
                    sb.AppendLine($"  Unit: {spec.Output.Unit}");
                }
            }
            else
            {
                sb.AppendLine("  (none)");
            }

            // Clarifying questions
            if (spec.ClarifyingQuestions != null && spec.ClarifyingQuestions.Count > 0)
            {
                sb.AppendLine("Clarifying Questions:");
                foreach (var question in spec.ClarifyingQuestions)
                {
                    sb.AppendLine($"  - {question}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a CodeSpecification into HTML for display in WebBrowser control.
        /// Delegates to <see cref="SpecHtmlRenderer.FormatSpecificationHtml"/>.
        /// </summary>
        public static string FormatSpecificationHtml(CodeSpecification spec)
            => SpecHtmlRenderer.FormatSpecificationHtml(spec);

        /// <summary>
        /// Gets the Claude API key from configuration or environment.
        /// Delegates to ClaudeApiClient to avoid duplication.
        /// </summary>
        private static string GetClaudeApiKey()
        {
            return ClaudeApiClient.GetClaudeApiKey();
        }

        /// <summary>
        /// Parses Claude API response JSON for usage tokens and tracks them.
        /// </summary>
        private static void TrackClaudeTokenUsage(string responseString, string callType, string model, string requestId)
        {
            try
            {
#if NET48
                var responseObj = JObject.Parse(responseString);
                var usage = responseObj["usage"];
                if (usage != null)
                {
                    int inTok = usage["input_tokens"]?.Value<int>() ?? 0;
                    int outTok = usage["output_tokens"]?.Value<int>() ?? 0;
                    TokenTracker.Track(callType, "claude", model, inTok, outTok, requestId);
                }
#else
                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    if (doc.RootElement.TryGetProperty("usage", out var usage))
                    {
                        int inTok = usage.TryGetProperty("input_tokens", out var inP) ? inP.GetInt32() : 0;
                        int outTok = usage.TryGetProperty("output_tokens", out var outP) ? outP.GetInt32() : 0;
                        TokenTracker.Track(callType, "claude", model, inTok, outTok, requestId);
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Log($"TrackClaudeTokenUsage error: {ex.Message}");
            }
        }

        private static void Log(string message)
        {
            Logger.Log("SpecGenerator", message);
        }
    }
}
