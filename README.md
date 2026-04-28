# BIBIM AI for Dynamo

> **Repository**: [github.com/SquareZero-Inc/bibim-dynamo](https://github.com/SquareZero-Inc/bibim-dynamo)

AI-powered Dynamo script generator and analyzer for Autodesk Revit.  
Generate and analyze Dynamo Python scripts using natural language — powered by Anthropic Claude, with OpenAI and Google Gemini routing wired in v1.0.2 (Anthropic is the field-verified path; full v1.1 verification of GPT-5.5 / Gemini 3.1 is in progress).

> **BYOK (Bring Your Own Key)**: BIBIM uses your own API keys. No subscription required.

---

## Features

- **Code Generation**: Describe what you want in natural language → get ready-to-run Dynamo Python code
- **Graph Analysis**: Analyze existing Dynamo graphs and get improvement suggestions
- **Local RAG**: Revit API documentation search runs **locally** from `RevitAPI.xml` — no external service required (since v1.0.1)
- **Multi-provider routing** (v1.0.2): Settings dialog accepts Anthropic / OpenAI / Google keys. Selecting GPT-5.5 or Gemini 3.1 routes the request to the matching provider via the factory in `Services/Providers/`. Anthropic Claude is the only path field-verified end-to-end on Dynamo workloads in this release; full v1.1 regression on the autofix loop and graph-analysis pipeline is pending.
- **Prompt caching** (v1.0.2): Anthropic prompt caching is on by default. Spec → codegen → autofix calls within a 5-minute window share a cached system prompt. `TokenTracker` exposes `SessionCacheHitRatio` for visibility.
- **Multi-version**: Supports Revit 2022–2027 / Dynamo 2.x–4.x

---

## Requirements

- Autodesk Revit 2022 or later with Dynamo installed
- An [Anthropic API key](https://console.anthropic.com/) (required for the field-verified path in v1.0.2)
- Optional (routes via factory in v1.0.2, full v1.1 verification pending): [OpenAI key](https://platform.openai.com/api-keys), [Google Gemini key](https://aistudio.google.com/apikey)

In-app: the API Key Setup dialog has a **"📖 View API Key Setup Guide"** button that opens a step-by-step Notion guide.

---

## Setup

### 1. Install

Download the latest release and run the installer, or manually copy the package folder to:

```
%APPDATA%\Dynamo\Dynamo Revit\<version>\packages\BIBIM_MVP\
```

### 2. Enter your API key

On first launch, BIBIM will prompt you for your Anthropic API key.  
You can update it anytime via the **⚙ Settings → API Key Settings** menu.

```
API key format: sk-ant-api03-...
Get one at: https://console.anthropic.com/
```

### 3. Revit API documentation search (local RAG)

BIBIM searches the Revit API **locally** from `RevitAPI.xml` — the file Autodesk ships with every Revit installation, and the same source the official online docs render from. No external service or extra setup required.

- **First-call build:** the index is built once per session in ~0.5 s, then cached for every subsequent search.
- Coverage: core DB, UI, MEP, Structure, IFC.
- v1.0.2 trim: top-K reduced 5 → 3 and chunk display capped at 1200 chars (member `Remarks` dropped) to keep RAG payload under ~3 KB per call.
- The Gemini key field in Settings is **no longer for RAG**; it is the LLM key routed to `:generateContent` when Gemini 3.1 Pro is the active model.

---

## Model Configuration

Open **⚙ Settings → API Key Settings** to select your model. Each model is annotated with an inline speed glyph (⚡⚡⚡ fast → ⚡ slow). Models without a registered provider key are disabled with a tooltip.

| Model | Provider | Status in v1.0.2 | Cost / session* |
|-------|----------|------------------|-----------------|
| **claude-sonnet-4-6** ⭐ | Anthropic | ✅ Field-verified | ~$0.05 |
| claude-opus-4-7 | Anthropic | ✅ Field-verified | ~$0.28 |
| gpt-5.5 | OpenAI | 🟡 Routed — full v1.1 verification pending | ~$0.10 |
| gemini-3.1-pro-preview | Google | 🟡 Routed — full v1.1 verification pending | ~$0.04 |

**Recommended: `claude-sonnet-4-6`** — best balance of quality, speed, and cost. With prompt caching enabled, repeated turns within a 5-minute window are cheaper than the table suggests.

> \* Estimated per code generation request (~7,000 input / 2,000 output tokens, **before** cache discount).  
> Actual cost depends on prompt complexity, conversation length, and prompt-cache hit ratio.  
> Prices are based on each provider's public pricing as of April 2026.

---

## Configuration File

BIBIM reads settings from `rag_config.json` in the package folder.  
A template is included as `rag_config.template.json`.

```json
{
  "claude_model": "claude-sonnet-4-6",
  "api_keys": {
    "anthropic_api_key": "sk-ant-...",
    "openai_api_key":    "sk-...",
    "gemini_api_key":    "AIzaSy..."
  }
}
```

(`claude_model` is the field name kept for backwards compatibility — it stores any selected model id.) You can edit this file directly, or use the Settings dialog in the UI. Upgrades from 1.0.1 are migrated automatically.

---

## Token usage & cost transparency (v1.0.2)

Anthropic prompt caching is enabled by default — within the 5-minute cache window, the system-prompt prefix is billed at the cached rate (`$0.30 / 1M`, 90% off the normal `$3 / 1M`). Spec → codegen → autofix calls in the same pipeline run share the cached prefix.

Cache effectiveness is logged per call and per session in `%APPDATA%\BIBIM\logs\bibim_debug.txt`:
```
[TokenTracker] rid=abc1234 type=codegen provider=claude in=520 out=210 cache_create=0 cache_read=2680
                session_total_in=4200 session_total_out=1850
                session_cache_create=2680 session_cache_read=8040
```
- `cache_read`: tokens served from cache (priced at 10% of normal input)
- `cache_create`: tokens written to cache on the first call (priced at 1.25× normal input — recovered on subsequent calls within 5 min)
- `SessionCacheHitRatio` (in `TokenTracker`): cache_read / (input + cache_read) for the current session

A typical multi-turn coding session sits at 60–80% hit ratio, which translates to ~30% lower bill vs uncached.

---

## Building from Source

### Prerequisites

- .NET 8 SDK or later (for Revit 2025/2026)
- .NET 10 SDK (for Revit 2027)
- Autodesk Revit + Dynamo installed (for NuGet package resolution)

### Build commands

```bash
# Debug (Revit 2025 + Dynamo 3.3.0)
dotnet build BIBIM_MVP.csproj -c Debug

# Release builds by Revit version
dotnet build BIBIM_MVP.csproj -c R2027_D402   # Revit 2027, Dynamo 27.0 (.NET 10)
dotnet build BIBIM_MVP.csproj -c R2026_D361   # Revit 2026, Dynamo 3.6.1 (.NET 8)
dotnet build BIBIM_MVP.csproj -c R2025_D330   # Revit 2025, Dynamo 3.3.0 (.NET 8)
dotnet build BIBIM_MVP.csproj -c R2024_D293   # Revit 2024, Dynamo 2.19.3 (.NET 4.8)

# Language variants (default: kr)
dotnet build BIBIM_MVP.csproj -c Debug -p:AppLanguage=en
```

Output: `bin/{language}/{configuration}/`

---

## Revit Version Support

| Revit | Dynamo | .NET | Build Config |
|-------|--------|------|--------------|
| 2027 | 27.0 | 10 | R2027_D402 |
| 2026 | 3.6.1 / 3.6.0 / 3.5.0 / 3.4.1 | 8 | R2026_D3xx |
| 2025 | 3.3.0 / 3.2.1 / 3.0.3 | 8 | R2025_D3xx |
| 2024 | 2.19.3 / 2.18.1 / 2.17.0 | 4.8 | R2024_D2xx |
| 2023 | 2.16.1 / 2.13.0 | 4.8 | R2023_D2xx |
| 2022 | 2.12.0 | 4.8 | R2022_D220 |

---

## Troubleshooting

### Debug Log

BIBIM writes a debug log to your local machine. Share this file when reporting issues.

**Log file location:**
```
C:\Users\<username>\AppData\Roaming\BIBIM\logs\bibim_debug.txt
```

Or open it directly by pasting this into Windows Explorer:
```
%APPDATA%\BIBIM\logs\bibim_debug.txt
```

- The log file is always enabled — no configuration needed.
- Rotates automatically at 5 MB (previous log saved as `bibim_debug.txt.old`).
- To report an issue, attach `bibim_debug.txt` (and `bibim_debug.txt.old` if relevant) to your GitHub issue.

---

## License

Apache License 2.0 — Copyright 2024 SquareZero Inc.  
See [LICENSE](LICENSE) for details.
