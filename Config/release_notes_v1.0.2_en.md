# BIBIM AI v1.0.2

**Release Date**: 2026-04-28

## What's New

### Multi-Provider Backend Wired
The settings dialog now shows three provider sections (Anthropic / OpenAI / Google) and four model options (Sonnet 4.6 / Opus 4.7 / GPT-5.5 / Gemini 3.1 Pro). The code generation pipeline routes the request to the matching provider based on the active model id — selecting GPT-5.5 sends to OpenAI's Chat Completions API, Gemini 3.1 Pro to `:generateContent`, etc. The factory + 3 adapters are in `Services/Providers/`.

> Anthropic Claude is the only provider field-verified end-to-end on Dynamo workloads in this build. OpenAI and Gemini routing is wired and compiles cleanly, but full regression coverage on the autofix loop and graph-analysis pipeline is scheduled for v1.1. If you hit issues with non-Anthropic models, swap back to a Claude model in Settings.

### Anthropic Prompt Caching
Spec → codegen → autofix calls within a 5-minute window now share a cached system prompt. Each request sends the system prompt as an array of text blocks tagged with `cache_control: ephemeral`, plus the `anthropic-beta: prompt-caching-2024-07-31` header. Per-call dynamic context (Revit API documentation, prior graph analysis) was moved out of the system prompt and into the active user message so the cache prefix stays bit-stable across calls.

`TokenTracker` now reads `usage.cache_creation_input_tokens` / `usage.cache_read_input_tokens` and exposes `SessionCacheCreationTokens`, `SessionCacheReadTokens`, and `SessionCacheHitRatio`. Watch the `[TokenTracker]` log lines after a few interactions to see the hit ratio climb.

### Slimmer Chat History per API Call
Long sessions no longer ship every prior turn to Claude. `BuildMessageHistory` now applies a 20-message trailing window before serialization, and `GetConversationHistoryForSpec` was wired to the same window so spec generation stops bypassing it. Codegen responses are also compressed when stored back into the in-memory history — the full Python body is replaced by `[Generated Dynamo Python script — ~N lines]` and only the GUIDE section is kept verbatim. `_conversationHistory` (UI / persistence) and `_contextManager` (raw recovery) still see the full content.

### Faster Pipeline (Verify Stage Removed)
The optional Gemini-based verify stage is gone. The pipeline is now: local BM25 RAG → Claude codegen → local validation → autofix loop. The verify stage relied on a SquareZero-owned fileSearch corpus that OSS users couldn't reach anyway, and the local validation gate was already catching the same class of issues. Removing it shaves up to ~25 s off the worst-case codegen latency when the Gemini key was set.

### Auto-Fix Shares the Codegen System Prompt
`RequestValidationAutoFixAsync` now uses `CodeGenSystemPrompt.Build(...)` instead of its own short fix-only system prompt, so the cache prefix is shared with the original codegen call — the autofix retry pays for ~30 fresh tokens instead of recreating a separate cache slot. `AutoFixRequestBuilder` no longer re-emits the Revit 2024+ breaking-changes block or the common API patterns block in the user message; those rules now live exclusively in the shared system prompt.

### Other Optimisations
- `max_tokens` differentiated per call type (spec / autofix = 2048, codegen = 4096, analysis = 3072) to reduce truncation while keeping budgets realistic.
- Local BM25 RAG tightened: `TopK` 5 → 3, chunk display cap 3000 → 1200 chars, member `Remarks` fields dropped.
- Graph analysis JSON serialisation switched to compact (`WriteIndented = false`).
- Legacy `RagService.cs`, `RagQueryPrompt.cs`, `RagVerificationPrompt.cs` deleted; `AnalysisService` migrated to `LocalDynamoRagService`.

### API Key Setup Guide Link
The API Key Setup dialog includes a **"📖 View API Key Setup Guide"** button at the top, opening a step-by-step Notion guide for getting keys from Anthropic / OpenAI / Google. The link is language-aware (KR or EN).

### Fully Localized Settings Dialog
The API Key Setup dialog used to render most labels (section titles, descriptions, "Active Model", "Cancel" / "Save", the "✓ Saved" badge, and the disabled-model tooltip) in English regardless of the build language. v1.0.2 routes every visible string through `LocalizationService` — the KR build now reads naturally end-to-end.

### Per-Model Speed Indicator
Each model radio in the API Key Setup dialog shows an inline ⚡ glyph next to the model name (Sonnet 4.6 = ⚡⚡⚡, Opus 4.7 / GPT-5.5 = ⚡⚡, Gemini 3.1 Pro = ⚡), backed by a localised hover tooltip. The trade-off between latency and depth is visible at the point of selection without needing to consult the docs.

## Bug Fixes / Improvements
- Loading state safety verified across all flows (spec generation, code generation, retry, question answers): `IsBusy` and `StatusText` are always cleared via `finally` blocks, so an API error never leaves the panel stuck on "Loading…".
- Cleaner save badge labels on the API key dialog when multiple keys are saved in the same session.
- `BIBIM-101` fixed: `GetConversationHistoryForSpec` now applies the same trailing window as `BuildMessageHistory`, so spec generation no longer ships full unbounded history.
- net48 (`R2024_D293`) build hardened: `BIBIM_Extension.cs` now imports `System.Threading.Tasks` explicitly (no longer relies on `ImplicitUsings`, which is .NET 8+).

## Requirements
- Autodesk Revit 2022 or later with Dynamo installed
- An Anthropic API key (required for the field-verified path)
- Optional: OpenAI / Google keys can be saved now and routed to the matching provider — full v1.1 verification pending

## Source
[github.com/SquareZero-Inc/bibim-dynamo](https://github.com/SquareZero-Inc/bibim-dynamo)
