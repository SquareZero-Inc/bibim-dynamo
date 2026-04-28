# Security Policy

## Reporting a vulnerability

**Do not open a public issue for security vulnerabilities.**

Email: dev@sqzr.team

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Any suggested fix (optional)

We'll acknowledge within 3 business days and aim to ship a fix within 14 days depending on severity.

## Scope

This policy covers the `SquareZero-Inc/bibim-dynamo` codebase.

The BIBIM extension handles:
- Your **provider API keys** (Anthropic, optionally OpenAI / Google Gemini), stored locally **next to the BIBIM_MVP DLL** in `rag_config.json`. The path resolves at runtime from `Assembly.Location`, so under a default Dynamo install it lives in:
  ```
  %APPDATA%\Dynamo\Dynamo Revit\<version>\packages\BIBIM_MVP\bin\rag_config.json
  ```
- Active Dynamo graph context passed to the active LLM provider for code generation or graph analysis
- Generated Python code executed inside the Dynamo Python Script node

None of this data leaves your machine except for outbound calls to the provider you've chosen:
- `api.anthropic.com` (when active model is `claude-*`)
- `api.openai.com` (when active model is `gpt-*` / `o1*` / `o3*`)
- `generativelanguage.googleapis.com` (when active model is `gemini-*`)

The first save of a key creates a one-time backup at `rag_config.json.bak` next to the active config.

## Out of scope

- Issues in third-party dependencies (report upstream)
- API key leakage caused by user sharing their own `rag_config.json` or `.bak` backup
- Plaintext storage of API keys in `rag_config.json` — this is a known limitation tracked as **BIBIM-301** in `TOKEN_OPT_BACKLOG.md`. DPAPI / Windows Credential Manager migration is on the v1.2 roadmap.
