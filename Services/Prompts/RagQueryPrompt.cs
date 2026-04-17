// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
namespace BIBIM_MVP
{
    /// <summary>
    /// RAG query prompt for fetching Revit API documentation.
    /// Extracted from GeminiService for maintainability.
    /// </summary>
    internal static class RagQueryPrompt
    {
        internal static string Build(string revitVersion, string specificationText)
        {
            return $@"
You are a Revit {revitVersion} API documentation expert for Dynamo Python scripting.

[SPECIFICATION TO IMPLEMENT]
{specificationText}

[YOUR TASK]
Search the Revit {revitVersion} API documentation and return ONLY the APIs directly needed for this specification.
Focus on:
1. Exact method signatures with ALL parameter types and return types
2. Which overloads exist (parameter count variations)
3. Whether methods are static or instance
4. Any Revit {revitVersion}-specific notes or restrictions

[OUTPUT FORMAT — be precise]
=== RELEVANT REVIT {revitVersion} API DOCUMENTATION ===

[Class: ClassName]
- Namespace: Autodesk.Revit.DB
- Method (instance): MethodName(ParamType1 name1, ParamType2 name2) -> ReturnType
- Method (static): StaticMethod(ParamType name) -> ReturnType
- Property (read-only): PropertyName -> Type
- Property (read-write): PropertyName -> Type

[VERSION NOTES for Revit {revitVersion}]
- Removed/changed APIs compared to previous versions
- Required alternatives

[CRITICAL RULES]
- Output ONLY APIs relevant to this specification — do NOT include unrelated classes
- Include EXACT parameter types (ElementId, not just 'id')
- If an overload has multiple signatures, list EACH separately
- Do NOT include C# syntax (var, new, ;) — describe types only
- Do NOT include code examples

If no relevant API documentation is found in the search results, return exactly: NO_RELEVANT_API_FOUND
";
        }
    }
}
