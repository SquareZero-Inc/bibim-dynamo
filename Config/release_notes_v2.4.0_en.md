# BIBIM AI v2.4.0 Release Notes

**Release Date**: 2026-04-06

## 🚀 Code Generation Quality + Usability Improvements

### Real-Time Pipeline Progress Display
- During code generation, the **current phase is now shown in real time**: "Searching API docs...", "Generating code...", "Verifying API usage...", "Validating code...", etc.
- Previously, only a generic "Generating code..." message was displayed, making it unclear which phase was taking time.

### Improved Auto-Fix Success Rate
- When code validation fails, the auto-fix strategy progressively intensifies with each retry attempt.
- If the first attempt doesn't resolve the issue, more aggressive fixes are tried, **improving the overall fix success rate**.
- Business logic is strictly preserved — only API compatibility issues are corrected.

### Automatic Python Node Input Port Adjustment
- Python Script node **input ports are automatically adjusted** to match the number of inputs required by the generated code.
- Reduces the need to manually add ports after node creation.

### Log System Improvements
- Log files have been moved to `%APPDATA%/BIBIM/logs/` to keep the user home directory clean.
- Log files are automatically rotated when they exceed 5MB.

### Internal Stability Improvements
- Improved the internal structure of the code generation pipeline for better stability and maintainability.
- Cleaned up unused legacy code.
- Expanded internationalization coverage.
