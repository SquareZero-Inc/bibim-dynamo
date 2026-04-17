# BIBIM AI v1.0.0 — Public OSS Release

**Release Date**: 2026-04-15

**BIBIM AI** is an AI-powered Dynamo script assistant for Autodesk Revit.
Generate, analyze, and debug Dynamo Python scripts using natural language — no subscription required.

## What's New

### BYOK (Bring Your Own Key)
- All AI inference runs directly from your own API keys — no server, no account, no subscription
- Supports **Anthropic Claude** (required) and **Google Gemini** (optional, for future RAG)
- Keys stored locally in `rag_config.json` next to the DLL; never transmitted to third parties

### AI Code Generation Pipeline
- Natural language → Dynamo Python script in seconds
- Spec confirmation step before generation — review what will be built before it runs
- Auto-fix loop: up to 2 automated repair passes with escalating strategies on validation failure
- RAG (Revit API doc search) is temporarily disabled in this OSS release — coming soon

### Node Graph Analysis
- One-click analysis of the current Dynamo graph
- Identifies issues, suggests improvements, explains node connections in plain language

### Multi-Version Support
| Revit | Dynamo | Runtime |
|-------|--------|---------|
| 2022–2024 | 2.x | .NET 4.8 |
| 2025 | 3.3.0 | .NET 8 |
| 2026 | 3.6.1 | .NET 8 |
| 2027 | 27.0 | .NET 10 |

### Session History
- Conversation history saved locally to `%APPDATA%/BIBIM/history/sessions.json`
- No cloud sync, no data leaves your machine

### Localization
- UI available in **Korean** and **English**
- Language selected at install time

## Requirements
- Autodesk Revit 2022 or later
- Claude API key ([console.anthropic.com](https://console.anthropic.com/))
- Google Gemini API key — optional, for RAG when available ([aistudio.google.com/apikey](https://aistudio.google.com/apikey))

## Installation
Run the installer (`BIBIM_AI_Setup_v1.0.0.exe`) and follow the prompts.
Enter your API key on first launch.

## Notes on RAG
RAG (Revit API documentation search via Gemini fileSearch) is **temporarily disabled** in this release.
The corpus was hosted under a private Google Cloud project; the store access model is being reworked for OSS users.
Code generation works normally without it — Claude's built-in Revit API knowledge handles the vast majority of cases.
A public-accessible RAG setup will be added in an upcoming release.

## Source
[github.com/SquareZero-Inc/bibim-dynamo](https://github.com/SquareZero-Inc/bibim-dynamo)
