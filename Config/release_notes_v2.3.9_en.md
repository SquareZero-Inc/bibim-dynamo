# BIBIM AI v2.3.9 Release Notes

**Release Date**: 2026-03-19

## 🛡️ Major AI Code Quality Improvements

### More Accurate Code Generation
- Code using **removed Revit APIs** (Element.LevelId, ElementId.IntegerValue, etc.) is now **immediately blocked**. Previously, these only triggered warnings and could cause runtime errors.
- Improved trust hierarchy for API reference documents, **reducing hallucinated code** caused by misleading reference materials.
- Auto-fix now includes removed API information, preventing the same mistakes from being repeated during correction.

### English Mode Fixes
- Spec-based code generation in English builds now properly applies **API document search and analysis context**. Previously, this was missing in English mode, resulting in lower code quality.
- Validation block messages are now displayed in the **correct build language** (Korean build → Korean, English build → English).

### Expanded Validation Coverage
- **Dynamo-specific APIs** (RevitServices, ProtoGeometry, DSCoreNodes) are now included in validation, catching a wider range of API errors before they reach your graph.
- Complex method calls with nested parentheses are now parsed accurately, **reducing false positive blocks**.

### Stability Improvements
- API document search now **automatically retries** once on transient failures before proceeding.
- A **dedicated 45-second timeout** for API document search prevents slow searches from delaying the entire response.
