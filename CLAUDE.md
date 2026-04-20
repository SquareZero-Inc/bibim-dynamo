# BIBIM AI — Dynamo Plugin

AI code generation plugin for Revit/Dynamo. Generates and analyzes Dynamo Python scripts from natural language.  
Backend: Claude (Anthropic) + Gemini (RAG, coming soon). OSS BYOK — no server, local JSON storage.

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
Single version source: `Directory.Build.props` (currently `2.4.1`)

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
│   ├── HistoryManager.cs        # OSS stub — no local history (uses LocalSessionManager)
│   ├── TokenTracker.cs          # In-memory token usage accumulator (per session)
│   ├── GeminiService.cs         # Pipeline orchestrator (RAG→Claude→validation)
│   ├── ClaudeApiClient.cs       # Claude API HTTP calls + token tracking
│   ├── RagService.cs            # Gemini RAG fetch/verify/cache/keyword extraction
│   ├── GenerationPipelineService.cs  # Pipeline phase → i18n status callbacks
│   ├── LocalCodeValidationService.cs # Code validation + ValidateAndFix
│   ├── LocalSessionManager.cs   # %APPDATA%/BIBIM/history/sessions.json management
│   ├── ConversationContextManager.cs # Session context / retry context management
│   └── AnalysisService.cs       # Node graph analysis (Claude + Gemini RAG)
├── ViewModels/
│   └── ChatWorkspaceViewModel.cs    # Main ViewModel
└── Views/
    ├── ChatWorkspace.xaml(.cs)  # Main chat UI (WebBrowser-based)
    ├── ApiKeySetupView.xaml(.cs) # BYOK API key setup dialog
    └── TopNavigationBar.xaml(.cs) # Top navigation bar
```

---

## Code Generation Pipeline

```
User input
  → SpecGenerator (spec generation, Claude)
  → User review/edit (JavaScript bridge → WPF)
  → GenerationPipelineService (phase status callbacks)
      → RagService.FetchRelevantApiDocsAsync (RAG, Gemini — coming soon)
      → ClaudeApiClient.CallClaudeApiAsync (code generation)
      → RagService.VerifyAndFixCodeAsync (RAG validation)
      → LocalCodeValidationService.ValidateAndFix (local validation)
      → On failure: AutoFixRequestBuilder → ClaudeApiClient retry (max 2, escalating strategy)
  → Return result
```

---

## Multi-Target Build Notes

- **net8.0** (Revit 2025+): `ImplicitUsings` enabled, modern C# syntax available
- **net48** (Revit 2022–2024): C# 7.3 limited, no `using` declarations, JSON via `Newtonsoft.Json`
- `#if NET48` / `#else` branches for dual JSON parsing throughout (`JsonHelper.cs` wrapper)
- `System.Text.Json` not available on net48 target

---

## Local Storage

| Path | Purpose |
|------|---------|
| `%APPDATA%/BIBIM/history/sessions.json` | Chat sessions (ChatSession / SingleMessage) |
| `%APPDATA%/BIBIM/logs/bibim_debug.txt` | Debug log (auto-rotation) |
| `{DLL location}/rag_config.json` | API keys, model, RAG store config |

---

## Localization

- String keys: `Config/i18n/kr.json`, `Config/i18n/en.json`
- XAML: `{loc:Loc KeyName}` (LocExtension)
- C#: `LocalizationService.Get("key")`, `LocalizationService.Format("key", args)`
- Language set at build time via `-p:AppLanguage=en|kr`, accessed at runtime via `AppLanguage.Current`

---

## App Startup Flow (OSS BYOK)

1. `AppLanguage.Initialize()` + `LocalizationService.Initialize()` — at startup
2. `ServiceContainer.Initialize()` — DI container init (registers `IVersionChecker` etc.)
3. `ClaudeApiClient.GetClaudeApiKey()` — check key from `rag_config.json`
4. No key → show `ApiKeySetupView` dialog → save → recheck
5. Key present → open `ChatWorkspace` window

---

## Technical Debt

- **DI migration**: `GeminiService`, `ConfigService` static → interface-based (planned)
- **Claude C# SDK adoption**: current manual RAG→generate→validate loop → Tool Use loop (planned)

---

## Key Files

| Purpose | File |
|---------|------|
| Version bump | `Directory.Build.props` |
| Add UI strings | `Config/i18n/kr.json` + `en.json` |
| Edit prompts | `Services/Prompts/` |
| API key / model config | `Config/rag_config.json` (gitignored — edit directly) |
| Release notes | `Config/release_notes_v*.md` |
