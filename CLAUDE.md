# BIBIM AI — Dynamo Plugin

AI code generation plugin for Revit/Dynamo. Generates and analyzes Dynamo Python scripts from natural language.  
Backend: Anthropic Claude (field-verified path). v1.0.2 wires OpenAI / Gemini routing through `LlmApiClientFactory` based on the selected model id, but full regression coverage on those providers is scheduled for v1.1. Local BM25 RAG over `RevitAPI.xml` (no external service). OSS BYOK — no server, local JSON storage.

---

## Build

```bash
# Development/debug (Revit 2025 + Dynamo 3.3.0, net8.0)
dotnet build BIBIM_MVP.csproj -c Debug

# Target-specific builds
dotnet build BIBIM_MVP.csproj -c R2026_D361   # Revit 2026, Dynamo 3.6.1 (net8.0)
dotnet build BIBIM_MVP.csproj -c R2025_D330   # Revit 2025, Dynamo 3.3.0 (net8.0)
dotnet build BIBIM_MVP.csproj -c R2024_D293   # Revit 2024, Dynamo 2.19.3 (net48)

# Language builds (default: kr)
dotnet build BIBIM_MVP.csproj -c Debug -p:AppLanguage=en
dotnet build BIBIM_MVP.csproj -c Debug -p:AppLanguage=kr
```

Output path: `bin/{language}/{Configuration}/`  
Single version source: `Directory.Build.props` (currently `1.0.2`)

---

## Architecture

```
BIBIM_AI/                            # Git root (this directory)
├── BIBIM_Extension.cs           # Dynamo IViewExtension entry point
├── Common/
│   ├── ServiceContainer.cs      # DI container (partial, mixed Singleton)
│   ├── ConfigService.cs         # rag_config.json loader (static)
│   └── Logger.cs
├── Config/
│   ├── rag_config.json          # API keys, model, RAG store config (.gitignore)
│   └── i18n/kr.json, en.json    # Localization strings
├── Models/
│   ├── SessionModels.cs         # ChatSession / SingleMessage / SessionContext etc.
│   ├── CodeSpecification.cs     # Spec DTO
│   └── GenerationResult.cs      # Pipeline result parsing (TYPE: protocol)
├── Services/
│   ├── Providers/               # Multi-provider LLM HTTP layer
│   │   ├── ILlmApiClient.cs     # Single-call provider interface (Dynamo is text-in/out)
│   │   ├── AnthropicApiClient.cs   # Anthropic Messages API + prompt-caching headers
│   │   ├── OpenAIApiClient.cs   # OpenAI Chat Completions adapter (routed in v1.0.2, full v1.1 verification pending)
│   │   ├── GeminiApiClient.cs   # Google :generateContent adapter (routed in v1.0.2, full v1.1 verification pending)
│   │   └── LlmApiClientFactory.cs  # model-id → provider routing
│   ├── HistoryManager.cs        # OSS stub — no local history (uses LocalSessionManager)
│   ├── TokenTracker.cs          # Per-session token + cache_create / cache_read accumulator + SessionCacheHitRatio
│   ├── GeminiService.cs         # Pipeline orchestrator: BM25 RAG → Claude → local validation → autofix. NOTE: name kept for back-compat; routes through LlmApiClientFactory based on the active model.
│   ├── ClaudeApiClient.cs       # Thin orchestrator: builds the (cacheable) system prompt + augments the last user message with RAG/analysis context, then dispatches via LlmApiClientFactory. Hosts MaxTokensSpec / MaxTokensAutoFix / MaxTokensCodegen / MaxTokensAnalysis budgets.
│   ├── LocalDynamoRagService.cs # Local BM25 RAG over RevitAPI.xml. v1.0.2 trim: TopK=3, MaxChunkDisplayChars=1200, no Remarks
│   ├── BM25Engine.cs            # Pure C# BM25, no NuGet deps
│   ├── AutoFixRequestBuilder.cs # Builds the user-side autofix payload. Reuses the codegen system prompt — no longer re-emits breaking-changes / common-API blocks.
│   ├── GenerationPipelineService.cs  # Pipeline phase → i18n status callbacks
│   ├── LocalCodeValidationService.cs # Code validation + ValidateAndFix
│   ├── LocalSessionManager.cs   # %APPDATA%/BIBIM/history/sessions.json management
│   ├── ConversationContextManager.cs # Session context / retry context management
│   └── AnalysisService.cs       # Node graph analysis (Claude + LocalDynamoRagService)
├── ViewModels/
│   └── ChatWorkspaceViewModel.cs    # Main ViewModel. Hosts MaxApiHistoryMessages window + CompressCodegenForHistory.
└── Views/
    ├── ChatWorkspace.xaml(.cs)  # Main chat UI (WebBrowser-based)
    ├── ApiKeySetupView.xaml(.cs) # BYOK key dialog: 3 provider sections + 4-model radio with key-presence gating
    └── TopNavigationBar.xaml(.cs) # Top navigation bar
```

---

## Code Generation Pipeline

```
User input
  → SpecGenerator.GenerateSpecificationAsync (windowed history via GetConversationHistoryForSpec)
  → User review/edit (JavaScript bridge → WPF)
  → GenerationPipelineService (phase status callbacks)
      → LocalDynamoRagService.FetchContextAsync (local BM25 over RevitAPI.xml — built once per session)
      → ClaudeApiClient.CallClaudeApiAsync — builds the static system prompt with cache_control,
        merges RAG + prior-analysis context into the last user message, dispatches via factory
      → LocalCodeValidationService.ValidateAndFix (local validation)
      → On failure: AutoFixRequestBuilder → ClaudeApiClient.RequestValidationAutoFixAsync
        (shares the codegen system prompt, max 2 attempts, escalating strategy)
  → Return result
```

Dynamo's pipeline is **single-call text-in/text-out** at every stage — no tool-use loop.
Multi-provider routing only swaps the HTTP client; the pipeline shape is unchanged.

The legacy Gemini fileSearch verify stage was removed in v1.0.2; local validation already
covers the same class of issues without a second LLM round-trip.

---

## Token / Cache Strategy (v1.0.2)

- **System prompt cacheability:** `AnthropicApiClient` sends `system` as an array of text blocks with `cache_control: ephemeral`, plus the `anthropic-beta: prompt-caching-2024-07-31` header. Per-call dynamic context is moved to the *user* message (`AugmentLastUserMessage`) so the system prefix stays bit-stable across calls.
- **Autofix shares the codegen system prompt** so the cache prefix is reused on every retry. Do not split it back into a separate fix-only system prompt or the cache miss returns.
- **Sliding history window:** `ChatWorkspaceViewModel.MaxApiHistoryMessages = 20`. Both `BuildMessageHistory` and `GetConversationHistoryForSpec` apply the same trailing window. `_conversationHistory` and `_contextManager` keep the full history; only the API payload is windowed.
- **Codegen response compression:** `CompressCodegenForHistory` reduces the stored Python body to `[Generated Dynamo Python script — ~N lines]`; the GUIDE block is preserved verbatim.
- **Cache observability:** `TokenTracker.SessionCacheCreationTokens`, `SessionCacheReadTokens`, `SessionCacheHitRatio`. Watch `[TokenTracker] cache_create=X cache_read=Y` log lines.

---

## Multi-Target Build Notes

- **net8.0** (Revit 2025+): `ImplicitUsings` enabled, modern C# syntax available
- **net48** (Revit 2022–2024): C# 7.3 limited, no `using` declarations, JSON via `Newtonsoft.Json`
- `#if NET48` / `#else` branches for dual JSON parsing throughout (`JsonHelper.cs` wrapper)
- `System.Text.Json` not available on net48 target. Switch expressions, recursive patterns, target-typed `new` are all .NET 8+ only — guard new code with `#if !NET48` or use the conservative form.
- `BIBIM_Extension.cs` imports `System.Threading.Tasks` explicitly so `Task.Run` works under net48 where `ImplicitUsings` is off.

---

## Local Storage

| Path | Purpose |
|------|---------|
| `%APPDATA%/BIBIM/history/sessions.json` | Chat sessions (ChatSession / SingleMessage) |
| `%APPDATA%/BIBIM/logs/bibim_debug.txt` | Debug log (auto-rotation, 5 MB) |
| `{DLL location}/rag_config.json` | API keys, active model, validation flags. Backed up to `rag_config.json.bak` on first save. |

---

## Localization

- String keys: `Config/i18n/kr.json`, `Config/i18n/en.json`
- XAML: `{loc:Loc KeyName}` (LocExtension)
- C#: `LocalizationService.Get("key")`, `LocalizationService.Format("key", args)`
- Language set at build time via `-p:AppLanguage=en|kr`, accessed at runtime via `AppLanguage.Current`
- **Caveat:** `AppLanguage.Initialize` should only be called once per process. Calling it a second time with a different language changes the system prompt text and silently busts the prompt-cache prefix (BIBIM-207 in `TOKEN_OPT_BACKLOG.md`).

---

## App Startup Flow (OSS BYOK)

1. `AppLanguage.Initialize()` + `LocalizationService.Initialize()` — at startup
2. `ServiceContainer.Initialize()` — DI container init (registers `IVersionChecker` etc.)
3. `ClaudeApiClient.GetClaudeApiKey()` — resolve key for the active model's provider
4. No key for the active provider → show `ApiKeySetupView` dialog → save → recheck
5. Key present → open `ChatWorkspace` window

---

## Loading-State Safety
- All `IsBusy = true` entry points in `ChatWorkspaceViewModel` are paired with `finally { IsBusy = false; _loadingCts?.Cancel(); StatusText = ""; }`. **Do not remove the finally blocks** — without them an LLM error leaves the panel stuck on "Loading…".
- Verified flows: `HandleSpecFirstFlowAsync`, `GenerateCodeFromSpecAsync`, `RetryCommand`, `HandleQuestionAnswers`, `ShowRetryLoading`/`HideRetryLoading`.

## Multi-Provider Model IDs (v1.0.2)
- Active model IDs are listed in `Common/ConfigService.cs:AvailableModels` and mirrored in `Views/ApiKeySetupView.xaml` (`Tag` attribute on each `RadioButton`). Keep the two in sync.
- **Gemini**: use vanilla `gemini-3.1-pro-preview`, NOT the `-customtools` variant. The customtools variant is specialised for agentic tool use and silently misbehaves on JSON-only output without registered tools — REVIT hit this and had to swap. Dynamo was always on vanilla; do not regress.
- The `gemini_model` JSON field (in `rag_config.json`) is dead config carried over from the legacy fileSearch RAG. `LocalDynamoRagService` (BM25) is the only RAG since v1.0.2; the field is read but no consumer uses it.

## Model Selector UX (v1.0.2)
- Each radio in `Views/ApiKeySetupView.xaml` shows a response-speed glyph (⚡⚡⚡ / ⚡⚡ / ⚡) inline next to the model name in `#FFD24D` (yellow). Tooltip text is localised via `ApiKeySetup_ModelSpeed_Fast/Medium/Slow` (set in `ApiKeySetupView.xaml.cs` `ApplyLocalization`).
- Speed assignments mirror REVIT: Sonnet 4.6 = ⚡⚡⚡, Opus 4.7 / GPT-5.5 = ⚡⚡, Gemini 3.1 Pro = ⚡. Update both projects together if benchmarks change.

---

## v1.1 Roadmap

- **Field-verify** OpenAI and Gemini routing on the autofix loop and graph-analysis pipeline. Routing is wired in v1.0.2 but lacks regression coverage.
- **Cache visibility for non-Anthropic providers**: `OpenAIApiClient` should read `usage.prompt_tokens_details.cached_tokens`; `GeminiApiClient` should read `usageMetadata.cachedContentTokenCount`. Both currently leave `CacheReadTokens = 0`. (BIBIM-204 in the backlog.)
- **HistorySummariser port** from REVIT — preserves intent of dropped early turns once the window cap is hit. (BIBIM-202.)
- **CodeArtifactCache** for precise follow-up edits (e.g. "remove the transaction from that script"). The compressed history loses the raw Python body; we need an out-of-band cache for the most recent generated artifact. (BIBIM-201.)
- See `TOKEN_OPT_BACKLOG.md` for the full Sprint 1 / Sprint 2 / Backlog breakdown.

The full reference implementation lives in `BIBIM_REVIT/Bibim.Core/Services/Providers/`.

---

## Technical Debt

- **`GeminiService` rename**: name kept for back-compat with persisted session payloads, but the class is the LLM-agnostic pipeline orchestrator. Rename to `CodeGenerationPipelineService` is BIBIM-303 in the backlog.
- **`ClaudeApiClient` static → interface**: blocks unit testing of the autofix loop. BIBIM-303.
- **DI migration**: `GeminiService`, `ConfigService` static → interface-based (planned).

---

## Key Files

| Purpose | File |
|---------|------|
| Version bump | `Directory.Build.props` |
| Add UI strings | `Config/i18n/kr.json` + `en.json` |
| Edit prompts | `Services/Prompts/CodeGenSystemPrompt.cs` (single source for codegen + autofix system prompt — keep static so cache_control sticks) |
| API key / model config | `Config/rag_config.json` (gitignored — edit directly) |
| Release notes | `Config/release_notes_v*.md` |
| Optimisation backlog | `TOKEN_OPT_BACKLOG.md` (live; check off as items ship) |
