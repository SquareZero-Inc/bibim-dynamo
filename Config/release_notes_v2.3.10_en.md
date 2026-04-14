# BIBIM AI v2.3.10 Release Notes

**Release Date**: 2026-03-20

## 🔧 Structural Stability + Technical Debt Cleanup

### Spec-Based Code Generation Path Stabilized
- Request detection now uses a **language-independent structured token** instead of localized string matching. Previously, encoding or language changes could cause detection failures.
- This structurally prevents the **English mode RAG/analysis context missing issue** from recurring.

### RAG Failure User Notification
- When API documentation search (RAG) fails, a **warning message is now shown to users**. Previously, failures were silently ignored, making it impossible to identify code quality degradation causes.
- **Automatic retry** is performed for transient errors (timeout, server errors), with a warning shown if retry also fails.
- "No match found" results are normal behavior and do not trigger retries.

### Codebase Cleanup
- Removed unused Legacy code (5 files) and unnecessary Wrapper classes (3 files) to streamline the codebase.
- Cleaned up 19 mojibake comment lines in `GeminiService.cs`.
- Project structure is now clearer, improving maintainability.

### API Validation Replay Regression Tests
- Added **19 replay test cases** based on real user failure scenarios.
- Covers Block (removed APIs, non-existent enums/members), Pass (valid code), Warn (deprecated), and Edge cases (nested parentheses, auto-fix).
- Automatically verifies that validation logic changes don't break existing behavior.

### GeminiService Prompt Separation
- Extracted code generation system prompt (~130 lines), RAG query prompt (~40 lines), and RAG verification prompt (~80 lines) into `Services/Prompts/` folder.
- Improves git diff readability for prompt changes and enables independent prompt rollbacks.

### Quality Management Infrastructure
- **Failure Log**: Documented 19 past bug cases and prohibited patterns to prevent recurring mistakes.
- **Release Checklist**: Standardized 4-stage verification process (build/API check/manual test/deploy).
- **Auto Build Check**: Automatically detects compilation errors on code changes.
