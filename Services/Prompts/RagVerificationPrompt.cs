namespace BIBIM_MVP
{
    /// <summary>
    /// RAG verification prompt for verifying and fixing Dynamo Python code.
    /// Extracted from GeminiService for maintainability.
    /// </summary>
    internal static class RagVerificationPrompt
    {
        internal static string Build(string revitVersion, string dynamoVersion, string pythonCode)
        {
            bool isIronPython = revitVersion == "2022";
            string pythonEngine = isIronPython ? "IronPython 2.7" : "CPython 3.x";

            string runtimeLockRules = isIronPython
                ? @"
**RUNTIME LOCK (MANDATORY):**
- Revit 2022 MUST use IronPython 2.7.
- If Python 3-only syntax appears, convert it to IronPython 2.7 compatible syntax."
                : @"
**RUNTIME LOCK (MANDATORY):**
- Revit 2023+ MUST use CPython 3.x.
- NEVER use IronPython-only syntax such as `except Exception, e`, `unicode(...)`, `xrange(...)`.
- Keep output strictly Python 3 compatible.";

            string ironPythonRules = isIronPython
                ? @"
**CRITICAL - IronPython 2.7 Compatibility:**
- Convert f-strings `f""text {var}""` → `""text {0}"".format(var)` or string concatenation
- Ensure no type hints like `def func(x: int):`
- Ensure no async/await syntax
- Use `.format()` or `%` for string formatting, NOT f-strings"
                : "";

            return $@"
You are a Revit {revitVersion} API expert. Your task is to VERIFY and FIX this Dynamo Python code.
Target runtime: Revit {revitVersion} + Dynamo {dynamoVersion} ({pythonEngine})

**VERIFICATION SCOPE (EXPANDED):**
Use RAG (File Search) to verify the following aspects:

1. **API Names & Existence**
   - Verify all class names exist in Revit {revitVersion} API
   - Verify all method names exist on their respective classes
   - Verify all property names exist and are accessible

2. **Method Signatures**
   - Verify parameter count matches the API definition
   - Verify parameter types are correct
   - Verify return types are handled correctly

3. **API Patterns & Best Practices**
   - Check if the API usage pattern is correct for Revit {revitVersion}
   - Verify transaction handling is appropriate
   - Check element unwrapping is done correctly

4. **Deprecated APIs**
   - Flag any deprecated APIs for Revit {revitVersion}
   - Suggest modern alternatives if available

5. **Breaking Changes for Revit {revitVersion}**
   - Check for version-specific breaking changes
   - Revit 2024+: `.IntegerValue` removed → use `.Value`
   - Revit 2025+: Check for any new API changes
   - Revit 2026+: Check for latest API modifications

6. **Python.NET Runtime Constraints**
   - XYZ operator overloads (-, +, *) do NOT work in CPython3
   - If code uses `point1 - point2` or similar XYZ arithmetic, replace with explicit XYZ constructor: `XYZ(a.X - b.X, a.Y - b.Y, a.Z - b.Z)`
   - Same applies to Add (+) and Multiply (*): use `XYZ(a.X + b.X, ...)` and `XYZ(a.X * s, ...)`
   - This is NOT a Revit API issue but a Python.NET limitation that RAG documents won't cover

**CRITICAL - PYTHON ONLY (NEVER C#):**
- The RAG documents contain C# examples, but you MUST output PYTHON code
- NEVER use C# syntax: `new`, `;`, `var`, `{{}}` braces, `//` comments
- KEEP Python syntax: `def`, `if:`, `for:`, `#` comments, indentation
- If RAG shows C# example like `element.get_Parameter(...)`, keep Python equivalent

**IMPORTANT:**
- Dynamo uses {pythonEngine}
{runtimeLockRules}
- Always use `DocumentManager`, `TransactionManager`, `UnwrapElement()`
{ironPythonRules}

**CODE TO VERIFY:**
```python
{pythonCode}
```

**OUTPUT RULES:**
- Return ONLY the corrected Python code (no C# syntax allowed)
- NO markdown code blocks (no ```)
- NO explanations or comments about what you changed
- Keep all original comments in their original language
- If the code is already correct, return it UNCHANGED
- Fix ALL issues found during verification (not just API names)";
        }
    }
}
