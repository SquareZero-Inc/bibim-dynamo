// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BIBIM_MVP
{
    internal static class AutoFixRequestBuilder
    {
        public static string BuildPrompt(string pythonCode, LocalValidationResult validation, string revitVersion, int attemptNo, int maxAttempts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are fixing Dynamo Python code for Revit.");
            sb.AppendLine("Return ONLY corrected Python code. Do not include markdown fences.");
            sb.AppendLine("Target Runtime: Revit " + revitVersion + " + Dynamo Python.");
            sb.AppendLine("Current Attempt: " + attemptNo + " / " + maxAttempts);
            sb.AppendLine();

            // Per-attempt strategy escalation
            if (attemptNo == 1)
            {
                sb.AppendLine("[STRATEGY: CONSERVATIVE] Fix only the listed BLOCKING ISSUES. Do not touch anything else.");
            }
            else if (attemptNo == 2)
            {
                sb.AppendLine("[STRATEGY: AGGRESSIVE] The conservative fix failed. Now also fix XYZ arithmetic, StorageType guards, and any suspicious API calls.");
                sb.AppendLine("Previous attempt applied conservative fixes but issues remain. Try a DIFFERENT approach.");
            }
            else
            {
                sb.AppendLine("[STRATEGY: REWRITE AFFECTED LOGIC] Previous attempts failed. Rewrite only the specific functions/blocks containing issues from scratch.");
                sb.AppendLine("Keep IN/OUT structure but rewrite the affected logic block completely using correct API patterns.");
            }
            sb.AppendLine();

            sb.AppendLine("[APPROVED FIX CATEGORIES — only these are allowed]");
            sb.AppendLine("1. API Symbol: Correct spelling/casing of Revit API names");
            sb.AppendLine("2. API Method Overload: Fix wrong parameter count or types");
            sb.AppendLine("3. Python.NET Runtime: Fix XYZ constructor, remove f-strings (IronPython), fix except syntax");
            sb.AppendLine("4. Transaction: Wrap/unwrap transaction scope — do NOT change which elements are modified");
            sb.AppendLine("5. Parameter Access: Add StorageType check, None check, use LookupParameter");
            sb.AppendLine();
            sb.AppendLine("[FORBIDDEN — do NOT change these under any circumstances]");
            sb.AppendLine("- Which elements are selected or filtered (FilteredElementCollector conditions)");
            sb.AppendLine("- Calculation algorithm or business logic order");
            sb.AppendLine("- IN[...] port usage or OUT value");
            sb.AppendLine("- Adding new features not present in the original code");
            sb.AppendLine();

            // #5: Include critical defense rules from system prompt to prevent hallucination during auto-fix
            int revitYear;
            if (int.TryParse(revitVersion, out revitYear) && revitYear >= 2024)
            {
                sb.AppendLine("[CRITICAL - Revit 2024+ API Breaking Changes]");
                sb.AppendLine("These APIs were REMOVED in Revit 2024. Do NOT introduce them during fix:");
                sb.AppendLine("- Element.LevelId -> REMOVED. Use: element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM).AsElementId()");
                sb.AppendLine("- ElementId.IntegerValue -> REMOVED. Use: elementId.Value (returns Int64)");
                sb.AppendLine("- CurveLoop(curves) constructor -> DOES NOT EXIST. Use: loop = CurveLoop() then loop.Append(curve)");
                sb.AppendLine("- Document.PlanTopologies -> REMOVED in 2024");
                sb.AppendLine();
                sb.AppendLine("[ABSOLUTE PROHIBITION]");
                sb.AppendLine("- NEVER use CurveLoop(anything) - CurveLoop() takes NO arguments");
                sb.AppendLine("- NEVER use element.LevelId - REMOVED");
                sb.AppendLine("- NEVER use elementId.IntegerValue - use .Value instead");
                sb.AppendLine();
            }

            sb.AppendLine("[Common API Patterns - MUST USE correct patterns]");
            sb.AppendLine("- FilteredElementCollector(doc, schedule.Id) for schedule elements");
            sb.AppendLine("- ExportDWGSettings.GetActivePredefinedSettings(doc) returns SINGLE object, NOT a list");
            sb.AppendLine("- doc.Export() is read-only, do NOT wrap in Transaction");
            sb.AppendLine("- Parameter reading: use LookupParameter(name) and check None + HasValue before AsDouble()/AsString()");
            sb.AppendLine("- XYZ arithmetic in CPython3: use XYZ(a.X+b.X, a.Y+b.Y, a.Z+b.Z), NOT a+b operator");
            sb.AppendLine("- Wall type access: use doc.GetElement(wall.GetTypeId()) AS WallType, NOT wall.WallType (2025+)");
            sb.AppendLine("- Do NOT open nested transactions — check if transaction is already active");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("[BLOCKING ISSUES]");

            List<ValidationIssue> blockIssues = validation.Issues
                .Where(i => i.Severity == ValidationSeverity.Block)
                .Take(20)
                .ToList();

            if (blockIssues.Count == 0)
            {
                sb.AppendLine("- No explicit block issue list found. Apply conservative API compatibility fixes only.");
            }
            else
            {
                foreach (var issue in blockIssues)
                {
                    string line = "- [" + issue.Category + "] " + issue.Symbol + " :: " + issue.Message;
                    if (issue.Candidates != null && issue.Candidates.Count > 0)
                    {
                        line += " | candidates: " + string.Join(", ", issue.Candidates.Take(3));
                    }
                    sb.AppendLine(line);
                }
            }

            bool hasAccessConstraint = blockIssues.Any(i => string.Equals(i.Category, "AccessConstraint", StringComparison.Ordinal));
            if (hasAccessConstraint)
            {
                sb.AppendLine();
                sb.AppendLine("[ACCESS CONSTRAINT FIX HINTS]");
                sb.AppendLine("- Do not use Type.Member form for instance members.");
                sb.AppendLine("- If a local variable starts with uppercase and looks like a type token (e.g., Category, Definition, Stairs), rename it to lowercase and use instance access.");
                sb.AppendLine("- Preserve IN/OUT behavior while applying the minimum rename/call-form fixes.");
                sb.AppendLine("- COMMON MISTAKE: ViewType.ToString() — ViewType is an enum TYPE, not an instance.");
                sb.AppendLine("  ❌ ViewType.ToString() == 'ThreeD'");
                sb.AppendLine("  ✅ active_view.ViewType == ViewType.ThreeD");
                sb.AppendLine("- COMMON MISTAKE: Category.Name — Category is a TYPE, not an instance.");
                sb.AppendLine("  ❌ Category.Name == 'Walls'");
                sb.AppendLine("  ✅ elem.Category.Name == 'Walls'");
                sb.AppendLine("- COMMON MISTAKE: Document.GetElement(id) — Document is a TYPE, not an instance.");
                sb.AppendLine("  ❌ Document.GetElement(elem_id)");
                sb.AppendLine("  ✅ doc.GetElement(elem_id)");
                sb.AppendLine("- General rule: Type.StaticMember is OK, Type.InstanceMember() is ALWAYS WRONG.");
            }

            bool hasXyzIssue = blockIssues.Any(i =>
                i.Symbol != null && (i.Symbol.Contains("XYZ") || i.Symbol.Contains("xyz")) ||
                i.Message != null && i.Message.Contains("operator"));
            if (hasXyzIssue)
            {
                sb.AppendLine();
                sb.AppendLine("[XYZ ARITHMETIC FIX]");
                sb.AppendLine("- CPython3 does NOT support XYZ operator overloads.");
                sb.AppendLine("- Replace: a + b  →  XYZ(a.X + b.X, a.Y + b.Y, a.Z + b.Z)");
                sb.AppendLine("- Replace: a - b  →  XYZ(a.X - b.X, a.Y - b.Y, a.Z - b.Z)");
                sb.AppendLine("- Replace: v * s  →  XYZ(v.X * s, v.Y * s, v.Z * s)");
            }

            bool hasParameterIssue = blockIssues.Any(i =>
                i.Category != null && i.Category.Contains("Parameter") ||
                i.Symbol != null && (i.Symbol.Contains("Parameter") || i.Symbol.Contains("param")));
            if (hasParameterIssue)
            {
                sb.AppendLine();
                sb.AppendLine("[PARAMETER ACCESS FIX]");
                sb.AppendLine("- Always use LookupParameter('name') not get_Parameter(string).");
                sb.AppendLine("- Always null-check: if param is not None and param.HasValue:");
                sb.AppendLine("- Use AsDouble(), AsString(), AsInteger(), AsElementId() matching the storage type.");
                sb.AppendLine("- Parameter.Set() returns bool — if not param.Set(value): handle failure.");
            }

            bool isIronPython = revitVersion == "2022";
            if (isIronPython)
            {
                sb.AppendLine("[IRONPYTHON 2.7 CONSTRAINTS]");
                sb.AppendLine("- Remove f-strings: f'{x}' → '{0}'.format(x)");
                sb.AppendLine("- Remove type hints: def foo(x: int) → def foo(x)");
                sb.AppendLine("- Fix except syntax: except Exception as e → except Exception, e");
                sb.AppendLine("- No walrus operator :=, no match/case, no positional-only params");
                sb.AppendLine();
            }

            sb.AppendLine("[ORIGINAL PYTHON CODE]");
            sb.AppendLine(pythonCode ?? "");
            sb.AppendLine();
            sb.AppendLine("[OUTPUT RULE]");
            sb.AppendLine("Output only the final Python code text.");

            return sb.ToString();
        }
    }
}
