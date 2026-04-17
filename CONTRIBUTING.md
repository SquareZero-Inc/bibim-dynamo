# Contributing to BIBIM AI for Dynamo

We built this open source to eliminate the repetitive manual work that BIM engineers deal with every day. Bug reports, feedback, and pull requests are all welcome.

---

## Bug Reports

Search existing issues before filing. If you're reporting something new, include all of the following:

1. **Revit version, Dynamo version, and BIBIM version**
2. **The prompt that triggered the issue** — paste it verbatim
3. **Debug log** (required): `%APPDATA%\BIBIM\logs\bibim_debug.txt`
4. **Codegen output folder** (if applicable): `%AppData%\BIBIM\debug\codegen\YYYYMMDD\`
   — redact any sensitive project data before attaching

Issues filed without logs may be deprioritized or closed.

If the extension fails to load, the Dynamo/Revit journal log usually has the reason:
```
%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <year>\Journals\
```

---

## Feature Requests

Before proposing something, ask yourself: is this a **generic BIM workflow** or a **company-specific standard**?

- **Generic BIM workflow** (e.g. "generate a node that filters elements by parameter", "analyze graph for redundant nodes"): open a GitHub Issue with the `enhancement` label.
- **Company-specific customization** (e.g. your firm's script naming conventions, internal RAG corpus, custom templates): this repo is not the right place. Contact us at **seokwoo.hong@sqzr.team** for enterprise options.

PRs that hardcode company-specific logic into core behavior won't be merged.

---

## Pull Requests

1. Fork the repo
2. Create a branch: `git checkout -b fix/your-bug` or `feature/your-feature`
3. Make your changes and test against at least one Revit + Dynamo version
4. Run the build: `dotnet build BIBIM_MVP.csproj --configuration R2026_D361`
5. Run tests: `dotnet test BIBIM_MVP.Tests/`
6. Commit and push, then open a PR against `main`

### Commit format

```
<type>: <subject>
```

Types: `feat` `fix` `refactor` `docs` `chore` `test`

### What gets merged

- Bug fixes with a clear description of the problem and how it's verified
- Generation quality improvements with a before/after explanation
- Dynamo/Revit version compatibility fixes

### What doesn't get merged

- Changes to the BYOK model (no subscriptions, no telemetry, no bundled keys)
- Architecture changes without a prior issue discussion
- Large unfocused PRs — split them

For anything that significantly touches the generation pipeline or context management, open an issue first.

---

## Prerequisites

| Tool | Requirement |
|------|-------------|
| Autodesk Revit | 2022 or later with Dynamo installed |
| .NET SDK | 4.8 (Revit 2022–2024) + 8 (Revit 2025–2026) + 10 (Revit 2027) |
| Visual Studio 2022 or dotnet CLI | |

---

## License

By contributing, you agree that your contributions will be licensed under the [Apache 2.0 License](LICENSE).
