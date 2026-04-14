# BIBIM AI v1.0.0 Release Notes

**Release Date**: 2026-04-15

## 🎉 Open Source Release (BYOK)

BIBIM AI is now open source.

### BYOK (Bring Your Own Key)
- Switched to a **bring-your-own-key model** — no subscription required.
- All you need is an Anthropic API key (required) and a Google Gemini API key (optional, for RAG).
- The API key setup dialog appears automatically on first launch.

### Features
- **Code Generation**: Describe what you need in natural language → get ready-to-run Dynamo Python scripts.
- **Graph Analysis**: Analyze existing Dynamo graphs and receive improvement suggestions.
- **RAG (optional)**: Revit API documentation lookup via Gemini for higher-accuracy code generation.
- **Multi-version support**: Revit 2022–2027 / Dynamo 2.x–4.x across all versions.

### Requirements
- Autodesk Revit 2022 or later with Dynamo installed
- Anthropic API key (https://console.anthropic.com/) — required
- Google Gemini API key (https://aistudio.google.com/) — optional, enables RAG
