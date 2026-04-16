# BIBIM AI for Dynamo

> **Repository**: [github.com/SquareZero-Inc/bibim-dynamo](https://github.com/SquareZero-Inc/bibim-dynamo)

AI-powered Dynamo script generator and analyzer for Autodesk Revit.  
Generate and analyze Dynamo Python scripts using natural language — powered by Claude (Anthropic).

> **BYOK (Bring Your Own Key)**: BIBIM uses your own API keys. No subscription required.

---

## Features

- **Code Generation**: Describe what you want in natural language → get ready-to-run Dynamo Python code
- **Graph Analysis**: Analyze existing Dynamo graphs and get improvement suggestions
- **RAG (optional)**: Revit API documentation lookup via Gemini for higher accuracy code
- **Multi-version**: Supports Revit 2022–2027 / Dynamo 2.x–4.x

---

## Requirements

- Autodesk Revit 2022 or later with Dynamo installed
- An [Anthropic API key](https://console.anthropic.com/) (required)
- A [Google Gemini API key](https://aistudio.google.com/apikey) (optional — enables RAG)

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

### 3. (Optional) Add Gemini key for RAG

If you add a Google Gemini API key, BIBIM will attempt to use Gemini's file search  
to look up Revit API documentation before generating code.

> **Note for OSS builds**: RAG requires access to a Gemini File Search corpus containing  
> Revit API docs. The official BIBIM release uses a SquareZero-hosted corpus (not publicly  
> accessible). If you are building from source, RAG calls will silently fail and BIBIM will  
> fall back to Claude-only generation. You can host your own corpus in Google AI Studio  
> and set `active_store` in `rag_config.json` to point to it.

```
Get a free Gemini key at: https://aistudio.google.com/apikey
```

---

## Recommended Model Configuration

Open **⚙ Settings → API Key Settings** to select your models.

### Claude (Code Generation)

| Model | Quality | Speed | Cost / session* |
|-------|---------|-------|-----------------|
| **claude-sonnet-4-6** ★ | ◎ Excellent | ◎ Fast | ~$0.05 |
| claude-opus-4-6 | ◎◎ Best | △ Slower | ~$0.25 |
| claude-haiku-4-5 | ○ Good | ◎◎ Fastest | ~$0.01 |

**Recommended: `claude-sonnet-4-6`** — best balance of quality, speed, and cost.

### Gemini (RAG — optional)

| Model | RAG Quality | Cost / session* |
|-------|-------------|-----------------|
| **gemini-2.0-flash** ★ | ○ Good | ~$0.001 |
| gemini-2.5-pro | ◎ Best | ~$0.008 |

**Recommended: `gemini-2.0-flash`** — fast and cheap. Upgrade to 2.5-pro if you need higher accuracy.

> \* Estimated per code generation request (~7,000 input / 2,000 output tokens for Claude).  
> Actual cost depends on prompt complexity and conversation length.  
> Prices are based on Anthropic and Google pricing as of April 2026.

---

## Configuration File

BIBIM reads settings from `rag_config.json` in the package folder.  
A template is included as `rag_config.template.json`.

```json
{
  "claude_model": "claude-sonnet-4-6",
  "gemini_model": "gemini-2.0-flash",
  "api_keys": {
    "claude_api_key": "sk-ant-...",
    "gemini_api_key": "AIza..."
  }
}
```

You can edit this file directly, or use the Settings dialog in the UI.

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
