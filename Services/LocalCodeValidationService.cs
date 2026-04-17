// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BIBIM_MVP
{
    internal enum ValidationSeverity
    {
        Info,
        Warning,
        Block
    }

    internal sealed class ValidationIssue
    {
        public ValidationSeverity Severity { get; set; }
        public string Category { get; set; }
        public string Symbol { get; set; }
        public string Message { get; set; }
        public List<string> Candidates { get; set; }
        /// <summary>
        /// Human-readable fix suggestion shown directly in the chat error message.
        /// Optional — null means use default candidate-based suggestion.
        /// </summary>
        public string FixSuggestion { get; set; }
    }

    internal sealed class LocalValidationResult
    {
        public bool IsPass { get; set; }
        public string ValidatedCode { get; set; }
        public bool CodeChanged { get; set; }
        public bool AutoFixApplied { get; set; }
        public string ApiIndexStatus { get; set; }
        public string XmlIndexStatus { get; set; }
        public List<ValidationIssue> Issues { get; set; }
        public int SymbolsTotal { get; set; }
        public int UnknownSymbols { get; set; }
        public int FixAttempts { get; set; }
        public string FinalStatus { get; set; }
        public List<string> ReferencedBuiltInParameters { get; set; }
        public List<string> ReferencedBuiltInCategories { get; set; }
        public List<string> ReferencedUnitTypeIds { get; set; }
        public List<string> MissingBuiltInParameters { get; set; }
        public List<string> MissingBuiltInCategories { get; set; }
        public List<string> MissingUnitTypeIds { get; set; }

        public int BlockCount
        {
            get { return Issues == null ? 0 : Issues.Count(i => i.Severity == ValidationSeverity.Block); }
        }

        public int WarningCount
        {
            get { return Issues == null ? 0 : Issues.Count(i => i.Severity == ValidationSeverity.Warning); }
        }
    }

    internal sealed class ValidationOptions
    {
        public string RolloutPhase { get; set; }
        public bool EnableApiXmlHints { get; set; }
        public int CurrentFixAttempt { get; set; }

        public static ValidationOptions Default()
        {
            return new ValidationOptions
            {
                RolloutPhase = "phase3",
                EnableApiXmlHints = true,
                CurrentFixAttempt = 0
            };
        }
    }

    internal static class LocalCodeValidationService
    {
        // Test-only: injectable API/XML indexes to bypass Revit runtime dependency
        private static RevitApiIndex _testApiIndex;
        private static RevitApiXmlIndex _testXmlIndex;

        /// <summary>
        /// Test-only: Set a mock API index for unit testing without Revit runtime.
        /// Call with null to reset.
        /// </summary>
        internal static void SetTestApiIndex(
            HashSet<string> builtInParameters = null,
            HashSet<string> builtInCategories = null,
            HashSet<string> unitTypeIds = null,
            HashSet<string> typeNames = null,
            Dictionary<string, HashSet<string>> typeMembers = null,
            Dictionary<string, HashSet<string>> typeStaticMembers = null,
            Dictionary<string, HashSet<string>> typeInstanceMembers = null,
            HashSet<string> deprecatedTypes = null,
            HashSet<string> deprecatedMembers = null)
        {
            _testApiIndex = new RevitApiIndex
            {
                IsReady = true,
                Status = "test_mock",
                AssemblyIdentity = "test",
                BuiltInParameters = builtInParameters ?? new HashSet<string>(StringComparer.Ordinal),
                BuiltInCategories = builtInCategories ?? new HashSet<string>(StringComparer.Ordinal),
                UnitTypeIds = unitTypeIds ?? new HashSet<string>(StringComparer.Ordinal),
                TypeNames = typeNames ?? new HashSet<string>(StringComparer.Ordinal),
                TypeMembers = typeMembers ?? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                TypeStaticMembers = typeStaticMembers ?? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                TypeInstanceMembers = typeInstanceMembers ?? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                TypeMethodSignatures = new Dictionary<string, Dictionary<string, List<CallableSignature>>>(StringComparer.Ordinal),
                TypeConstructorSignatures = new Dictionary<string, List<CallableSignature>>(StringComparer.Ordinal),
                DeprecatedTypes = deprecatedTypes ?? new HashSet<string>(StringComparer.Ordinal),
                DeprecatedMembers = deprecatedMembers ?? new HashSet<string>(StringComparer.Ordinal)
            };
        }

        /// <summary>
        /// Test-only: Set a mock XML index for unit testing.
        /// </summary>
        internal static void SetTestXmlIndex(
            HashSet<string> deprecatedTypes = null,
            HashSet<string> deprecatedMembers = null)
        {
            _testXmlIndex = new RevitApiXmlIndex
            {
                Status = "test_mock",
                SourcePath = "test",
                DeprecatedTypes = deprecatedTypes ?? new HashSet<string>(StringComparer.Ordinal),
                DeprecatedMembers = deprecatedMembers ?? new HashSet<string>(StringComparer.Ordinal),
                TypeSummaries = new Dictionary<string, string>(StringComparer.Ordinal),
                MemberSummaries = new Dictionary<string, string>(StringComparer.Ordinal)
            };
        }

        /// <summary>
        /// Test-only: Clear injected test indexes.
        /// </summary>
        internal static void ClearTestIndexes()
        {
            _testApiIndex = null;
            _testXmlIndex = null;
        }

        private static readonly Regex BuiltInParameterRegex =
            new Regex(@"\bBuiltInParameter\.([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        private static readonly Regex BuiltInCategoryRegex =
            new Regex(@"\bBuiltInCategory\.([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        private static readonly Regex UnitTypeIdRegex =
            new Regex(@"\bUnitTypeId\.([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        private static readonly Regex TypeMemberRegex =
            new Regex(@"\b([A-Z][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        // Atomic groups (?>...) prevent catastrophic backtracking on unclosed parentheses
        private static readonly Regex TypeMethodCallRegex =
            new Regex(@"\b([A-Z][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*\((?>(?:[^()\r\n]|\((?>(?:[^()\r\n]|\([^()\r\n]*\))*)\))*)\)", RegexOptions.Compiled);

        private static readonly Regex TypeCtorRegex =
            new Regex(@"(?<!\.)\b([A-Z][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

        // Atomic groups (?>...) prevent catastrophic backtracking on unclosed parentheses
        private static readonly Regex TypeCtorCallRegex =
            new Regex(@"(?<!\.)\b([A-Z][A-Za-z0-9_]*)\s*\((?>(?:[^()\r\n]|\((?>(?:[^()\r\n]|\([^()\r\n]*\))*)\))*)\)", RegexOptions.Compiled);

        private static readonly Regex ImportFromRegex =
            new Regex(@"^\s*from\s+([A-Za-z0-9_\.]+)\s+import\s+(.+)$", RegexOptions.Compiled);

        private static readonly Regex ImportRegex =
            new Regex(@"^\s*import\s+([A-Za-z0-9_\.]+)(?:\s+as\s+([A-Za-z0-9_]+))?\s*$", RegexOptions.Compiled);

        private static readonly Regex CpythonExceptRegex =
            new Regex(@"except\s+[A-Za-z_][A-Za-z0-9_]*\s*,\s*[A-Za-z_][A-Za-z0-9_]*\s*:", RegexOptions.Compiled);

        private static readonly Regex ReadOnlySetRegex =
            new Regex(@"\b([A-Za-z_][A-Za-z0-9_]*)\.Set\s*\(", RegexOptions.Compiled);

        private static readonly Regex ParameterAssignRegex =
            new Regex(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*=\s*.*get_Parameter\s*\(", RegexOptions.Compiled);

        private static readonly Regex ReadOnlyGuardRegex =
            new Regex(@"\bnot\s+([A-Za-z_][A-Za-z0-9_]*)\.IsReadOnly\b|\b([A-Za-z_][A-Za-z0-9_]*)\.IsReadOnly\s*==\s*False\b", RegexOptions.Compiled);

        private static readonly Regex LocalAssignRegex =
            new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=", RegexOptions.Compiled);

        private static readonly Regex LocalForVarRegex =
            new Regex(@"^\s*for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\b", RegexOptions.Compiled);

        private static readonly HashSet<string> NonRevitTypeAllowList = new HashSet<string>(StringComparer.Ordinal)
        {
            "DocumentManager",
            "TransactionManager",
            "UnwrapElement",
            // #10: Removed List, Dictionary from full skip - they now get basic member validation
            "Tuple",
            "Regex",
            "System",
            "Math",
            "Convert",
            "Path",
            "File",
            "Exception",
            "Guid",
            "Logger",
            "Stopwatch",
            "StringBuilder"
        };

        // #10: Types that skip Revit API index validation but still get basic .NET member checks
        private static readonly HashSet<string> DotNetTypeAllowList = new HashSet<string>(StringComparer.Ordinal)
        {
            "List",
            "Dictionary"
        };

        public static LocalValidationResult ValidateAndFix(string pythonCode, string revitVersion)
        {
            return ValidateAndFix(pythonCode, revitVersion, ValidationOptions.Default());
        }

        public static LocalValidationResult ValidateAndFix(string pythonCode, string revitVersion, ValidationOptions options)
        {
            if (pythonCode == null)
            {
                return new LocalValidationResult
                {
                    IsPass = false,
                    ValidatedCode = "",
                    CodeChanged = false,
                    AutoFixApplied = false,
                    ApiIndexStatus = "code_empty",
                    XmlIndexStatus = "not_loaded",
                    SymbolsTotal = 0,
                    UnknownSymbols = 0,
                    FixAttempts = 0,
                    FinalStatus = "blocked",
                    ReferencedBuiltInParameters = new List<string>(),
                    ReferencedBuiltInCategories = new List<string>(),
                    ReferencedUnitTypeIds = new List<string>(),
                    MissingBuiltInParameters = new List<string>(),
                    MissingBuiltInCategories = new List<string>(),
                    MissingUnitTypeIds = new List<string>(),
                    Issues = new List<ValidationIssue>
                    {
                        new ValidationIssue
                        {
                            Severity = ValidationSeverity.Block,
                            Category = "Input",
                            Symbol = "code",
                            Message = "Code is empty.",
                            Candidates = new List<string>()
                        }
                    }
                };
            }

            return ValidateCore(pythonCode, revitVersion, true, options ?? ValidationOptions.Default());
        }

        private static LocalValidationResult ValidateCore(string pythonCode, string revitVersion, bool allowAutoFix, ValidationOptions options)
        {
            var apiIndex = _testApiIndex ?? RevitApiIndexCache.GetOrCreate();
            var xmlIndex = _testXmlIndex ?? (options.EnableApiXmlHints ? RevitApiXmlProvider.GetOrLoad() : null);
            var extracted = SymbolExtractionResult.Extract(pythonCode);
            var issues = new List<ValidationIssue>();
            int symbolsTotal = extracted.GetTotalSymbolCount();
            var enumDebug = BuildEnumDebugSnapshot(extracted, apiIndex);

            if (!apiIndex.IsReady)
            {
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "ApiIndex",
                    Symbol = "RevitAPI",
                    Message = "RevitAPI assembly is not loaded, local API validation is unavailable.",
                    Candidates = new List<string>()
                });

                return new LocalValidationResult
                {
                    IsPass = false,
                    ValidatedCode = pythonCode,
                    CodeChanged = false,
                    AutoFixApplied = false,
                    ApiIndexStatus = apiIndex.Status,
                    XmlIndexStatus = xmlIndex == null ? "disabled" : xmlIndex.Status,
                    SymbolsTotal = symbolsTotal,
                    UnknownSymbols = 1,
                    FixAttempts = options.CurrentFixAttempt,
                    FinalStatus = "blocked",
                    ReferencedBuiltInParameters = enumDebug.ReferencedBuiltInParameters,
                    ReferencedBuiltInCategories = enumDebug.ReferencedBuiltInCategories,
                    ReferencedUnitTypeIds = enumDebug.ReferencedUnitTypeIds,
                    MissingBuiltInParameters = enumDebug.MissingBuiltInParameters,
                    MissingBuiltInCategories = enumDebug.MissingBuiltInCategories,
                    MissingUnitTypeIds = enumDebug.MissingUnitTypeIds,
                    Issues = issues
                };
            }

            string phase = NormalizeRolloutPhase(options.RolloutPhase);
            bool enablePhase2 = phase == "phase2" || phase == "phase3";
            bool enablePhase3 = phase == "phase3";

            ValidateEnumSymbols(extracted.BuiltInParameters, apiIndex.BuiltInParameters, "BuiltInParameter", issues);
            ValidateEnumSymbols(extracted.BuiltInCategories, apiIndex.BuiltInCategories, "BuiltInCategory", issues);
            ValidateEnumSymbols(extracted.UnitTypeIds, apiIndex.UnitTypeIds, "UnitTypeId", issues);
            ValidateEnumAliasSymbols(extracted.TypeMemberRefs, extracted.TypeAliasMap, apiIndex, issues);

            if (enablePhase2)
            {
                ValidateTypeMembers(extracted.TypeMemberRefs, extracted.ImportModules, extracted.TypeAliasMap, apiIndex, issues);
                ValidateMethodSignatures(extracted.TypeMethodCallRefs, extracted.ImportModules, extracted.TypeAliasMap, apiIndex, issues);
                ValidateTypeConstructors(
                    extracted.TypeConstructorRefs,
                    extracted.TypeConstructorCallRefs,
                    extracted.ImportModules,
                    extracted.TypeAliasMap,
                    apiIndex,
                    issues);
                ValidateAccessConstraints(
                    extracted.TypeMemberRefs,
                    extracted.ImportModules,
                    extracted.TypeAliasMap,
                    extracted.LocalIdentifiers,
                    apiIndex,
                    pythonCode,
                    issues);
            }

            if (enablePhase3)
            {
                ValidateVersionCompatibility(extracted.TypeMemberRefs, extracted.TypeConstructorRefs, extracted.TypeAliasMap, apiIndex, xmlIndex, issues);
                ValidatePythonRuntimeRules(pythonCode, revitVersion, issues);
                ValidateRevitUsageRules(pythonCode, revitVersion, issues);
            }

            if (allowAutoFix)
            {
                string fixedCode = TryAutoFixEnums(pythonCode, issues, apiIndex);
                fixedCode = TryAutoFixAccessConstraints(fixedCode, issues);
                if (!string.Equals(fixedCode, pythonCode, StringComparison.Ordinal))
                {
                    var rerunOptions = new ValidationOptions
                    {
                        RolloutPhase = options.RolloutPhase,
                        EnableApiXmlHints = options.EnableApiXmlHints,
                        CurrentFixAttempt = options.CurrentFixAttempt + 1
                    };
                    var rerun = ValidateCore(fixedCode, revitVersion, false, rerunOptions);
                    rerun.CodeChanged = true;
                    rerun.AutoFixApplied = true;
                    return rerun;
                }
            }

            int unknownSymbols = issues.Count(i =>
                i.Severity == ValidationSeverity.Block &&
                (i.Category == "BuiltInParameter" ||
                 i.Category == "BuiltInCategory" ||
                 i.Category == "UnitTypeId" ||
                 i.Category == "Type" ||
                 i.Category == "Member" ||
                 i.Category == "Signature"));

            return new LocalValidationResult
            {
                IsPass = issues.All(i => i.Severity != ValidationSeverity.Block),
                ValidatedCode = pythonCode,
                CodeChanged = false,
                AutoFixApplied = false,
                ApiIndexStatus = apiIndex.Status,
                XmlIndexStatus = xmlIndex == null ? "disabled" : xmlIndex.Status,
                SymbolsTotal = symbolsTotal,
                UnknownSymbols = unknownSymbols,
                FixAttempts = options.CurrentFixAttempt,
                FinalStatus = issues.Any(i => i.Severity == ValidationSeverity.Block)
                    ? "blocked"
                    : (issues.Any(i => i.Severity == ValidationSeverity.Warning) ? "warn" : "pass"),
                ReferencedBuiltInParameters = enumDebug.ReferencedBuiltInParameters,
                ReferencedBuiltInCategories = enumDebug.ReferencedBuiltInCategories,
                ReferencedUnitTypeIds = enumDebug.ReferencedUnitTypeIds,
                MissingBuiltInParameters = enumDebug.MissingBuiltInParameters,
                MissingBuiltInCategories = enumDebug.MissingBuiltInCategories,
                MissingUnitTypeIds = enumDebug.MissingUnitTypeIds,
                Issues = issues
            };
        }

        private static void ValidateEnumSymbols(
            IEnumerable<string> symbols,
            HashSet<string> indexSet,
            string category,
            List<ValidationIssue> issues)
        {
            foreach (var symbol in symbols.Distinct(StringComparer.Ordinal))
            {
                if (indexSet.Contains(symbol))
                    continue;

                var candidates = SuggestCandidates(symbol, indexSet, 3);
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = category,
                    Symbol = symbol,
                    Message = category + " symbol does not exist in this Revit runtime.",
                    Candidates = candidates,
                    FixSuggestion = candidates != null && candidates.Count > 0
                        ? "Replace with: " + candidates[0]
                        : null
                });
            }
        }

        private static void ValidateEnumAliasSymbols(
            IEnumerable<TypeMemberRef> typeMembers,
            Dictionary<string, string> typeAliasMap,
            RevitApiIndex index,
            List<ValidationIssue> issues)
        {
            foreach (var reference in typeMembers)
            {
                string resolvedType = ResolveTypeAlias(reference.TypeName, typeAliasMap);
                if (string.Equals(resolvedType, "BuiltInParameter", StringComparison.Ordinal))
                {
                    ValidateAliasedEnumValue(reference.MemberName, "BuiltInParameter", index.BuiltInParameters, issues);
                }
                else if (string.Equals(resolvedType, "BuiltInCategory", StringComparison.Ordinal))
                {
                    ValidateAliasedEnumValue(reference.MemberName, "BuiltInCategory", index.BuiltInCategories, issues);
                }
                else if (string.Equals(resolvedType, "UnitTypeId", StringComparison.Ordinal))
                {
                    ValidateAliasedEnumValue(reference.MemberName, "UnitTypeId", index.UnitTypeIds, issues);
                }
            }
        }

        private static void ValidateAliasedEnumValue(
            string valueName,
            string category,
            HashSet<string> indexSet,
            List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(valueName) || indexSet.Contains(valueName))
                return;

            var candidates = SuggestCandidates(valueName, indexSet, 3);
            AddIssue(issues, new ValidationIssue
            {
                Severity = ValidationSeverity.Block,
                Category = category,
                Symbol = valueName,
                Message = category + " symbol does not exist in this Revit runtime.",
                Candidates = candidates,
                FixSuggestion = candidates != null && candidates.Count > 0
                    ? "Replace with: " + candidates[0]
                    : null
            });
        }

        private static void ValidateTypeMembers(
            IEnumerable<TypeMemberRef> typeMembers,
            Dictionary<string, string> importModules,
            Dictionary<string, string> typeAliasMap,
            RevitApiIndex index,
            List<ValidationIssue> issues)
        {
            foreach (var reference in typeMembers)
            {
                if (string.IsNullOrWhiteSpace(reference.TypeName) || string.IsNullOrWhiteSpace(reference.MemberName))
                    continue;

                string resolvedType = ResolveTypeAlias(reference.TypeName, typeAliasMap);

                if (IsEnumTypeName(resolvedType))
                    continue;

                if (IsModuleAlias(reference.TypeName, importModules, typeAliasMap))
                    continue;

                if (!ShouldCheckAsRevitType(resolvedType, importModules, typeAliasMap))
                    continue;

                if (!index.TypeNames.Contains(resolvedType))
                {
                    var severity = IsExplicitRevitTypeReference(reference.TypeName, importModules, typeAliasMap)
                        ? ValidationSeverity.Block
                        : ValidationSeverity.Warning;

                    var typeCandidates = SuggestCandidates(resolvedType, index.TypeNames, 3);
                    AddIssue(issues, new ValidationIssue
                    {
                        Severity = severity,
                        Category = "Type",
                        Symbol = resolvedType,
                        Message = "Type is not found in this Revit runtime API.",
                        Candidates = typeCandidates,
                        FixSuggestion = typeCandidates != null && typeCandidates.Count > 0
                            ? "Did you mean: " + typeCandidates[0]
                            : null
                    });
                    continue;
                }

                HashSet<string> members;
                if (!index.TypeMembers.TryGetValue(resolvedType, out members))
                    continue;

                if (!members.Contains(reference.MemberName))
                {
                    var memberCandidates = SuggestCandidates(reference.MemberName, members, 3);
                    AddIssue(issues, new ValidationIssue
                    {
                        Severity = ValidationSeverity.Block,
                        Category = "Member",
                        Symbol = resolvedType + "." + reference.MemberName,
                        Message = "Member is not found on the type in this Revit runtime API.",
                        Candidates = memberCandidates,
                        FixSuggestion = memberCandidates != null && memberCandidates.Count > 0
                            ? "Try: " + resolvedType + "." + memberCandidates[0]
                            : null
                    });
                }
            }
        }

        private static void ValidateMethodSignatures(
            IEnumerable<TypeMethodCallRef> methodCalls,
            Dictionary<string, string> importModules,
            Dictionary<string, string> typeAliasMap,
            RevitApiIndex index,
            List<ValidationIssue> issues)
        {
            foreach (var call in methodCalls)
            {
                if (string.IsNullOrWhiteSpace(call.TypeName) || string.IsNullOrWhiteSpace(call.MethodName))
                    continue;

                string resolvedType = ResolveTypeAlias(call.TypeName, typeAliasMap);

                if (IsEnumTypeName(resolvedType))
                    continue;

                if (IsModuleAlias(call.TypeName, importModules, typeAliasMap))
                    continue;

                if (!ShouldCheckAsRevitType(resolvedType, importModules, typeAliasMap))
                    continue;

                if (!index.TypeNames.Contains(resolvedType))
                    continue;

                Dictionary<string, List<CallableSignature>> methodMap;
                if (!index.TypeMethodSignatures.TryGetValue(resolvedType, out methodMap))
                    continue;

                List<CallableSignature> signatures;
                if (!methodMap.TryGetValue(call.MethodName, out signatures))
                    continue;

                if (call.ArgumentCount < 0)
                    continue;

                bool isValid = signatures.Any(s => s.Accepts(call.ArgumentCount));
                if (isValid)
                    continue;

                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "Signature",
                    Symbol = resolvedType + "." + call.MethodName + "(" + call.ArgumentCount + ")",
                    Message = "No overload matches the argument count in this Revit runtime API.",
                    Candidates = BuildSignatureCandidates(signatures, 3)
                });
            }
        }

        private static void ValidateTypeConstructors(
            IEnumerable<string> constructorTypeNames,
            IEnumerable<TypeConstructorCallRef> constructorCalls,
            Dictionary<string, string> importModules,
            Dictionary<string, string> typeAliasMap,
            RevitApiIndex index,
            List<ValidationIssue> issues)
        {
            foreach (var typeName in constructorTypeNames.Distinct(StringComparer.Ordinal))
            {
                string resolvedType = ResolveTypeAlias(typeName, typeAliasMap);

                if (IsModuleAlias(typeName, importModules, typeAliasMap))
                    continue;

                if (!ShouldCheckAsRevitType(resolvedType, importModules, typeAliasMap))
                    continue;

                if (!index.TypeNames.Contains(resolvedType))
                {
                    var severity = IsExplicitRevitTypeReference(typeName, importModules, typeAliasMap)
                        ? ValidationSeverity.Block
                        : ValidationSeverity.Warning;

                    AddIssue(issues, new ValidationIssue
                    {
                        Severity = severity,
                        Category = "Type",
                        Symbol = resolvedType,
                        Message = "Constructor type is not found in this Revit runtime API.",
                        Candidates = SuggestCandidates(resolvedType, index.TypeNames, 3)
                    });
                }
            }

            foreach (var call in constructorCalls)
            {
                string resolvedType = ResolveTypeAlias(call.TypeName, typeAliasMap);
                if (string.IsNullOrWhiteSpace(resolvedType))
                    continue;

                if (IsModuleAlias(call.TypeName, importModules, typeAliasMap))
                    continue;

                if (!ShouldCheckAsRevitType(resolvedType, importModules, typeAliasMap))
                    continue;

                if (!index.TypeNames.Contains(resolvedType))
                    continue;

                if (call.ArgumentCount < 0)
                    continue;

                List<CallableSignature> signatures;
                if (!index.TypeConstructorSignatures.TryGetValue(resolvedType, out signatures))
                    continue;

                if (signatures.Count == 0)
                    continue;

                bool isValid = signatures.Any(s => s.Accepts(call.ArgumentCount));
                if (isValid)
                    continue;

                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "Signature",
                    Symbol = resolvedType + ".ctor(" + call.ArgumentCount + ")",
                    Message = "No constructor overload matches the argument count in this Revit runtime API.",
                    Candidates = BuildSignatureCandidates(signatures, 3)
                });
            }
        }

        private static void ValidateAccessConstraints(
            IEnumerable<TypeMemberRef> typeMembers,
            Dictionary<string, string> importModules,
            Dictionary<string, string> typeAliasMap,
            HashSet<string> localIdentifiers,
            RevitApiIndex index,
            string code,
            List<ValidationIssue> issues)
        {
            // Static call form (Type.Member) on instance-only members.
            foreach (var reference in typeMembers)
            {
                if (string.IsNullOrWhiteSpace(reference.TypeName) || string.IsNullOrWhiteSpace(reference.MemberName))
                    continue;
                if (IsLikelyLocalIdentifier(reference.TypeName, localIdentifiers))
                    continue;

                string resolvedType = ResolveTypeAlias(reference.TypeName, typeAliasMap);
                if (IsEnumTypeName(resolvedType))
                    continue;
                if (IsModuleAlias(reference.TypeName, importModules, typeAliasMap))
                    continue;
                if (!index.TypeNames.Contains(resolvedType))
                    continue;

                HashSet<string> instanceMembers;
                if (index.TypeInstanceMembers.TryGetValue(resolvedType, out instanceMembers) &&
                    instanceMembers.Contains(reference.MemberName))
                {
                    AddIssue(issues, new ValidationIssue
                    {
                        Severity = ValidationSeverity.Block,
                        Category = "AccessConstraint",
                        Symbol = resolvedType + "." + reference.MemberName,
                        Message = "Instance member is accessed with a static type call form.",
                        Candidates = new List<string> { "Use an instance: obj." + reference.MemberName + "(...)" }
                    });
                }
            }

            // Parameter.Set(...) without IsReadOnly guard.
            var paramVars = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in ParameterAssignRegex.Matches(code))
            {
                if (m.Groups.Count > 1 && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                    paramVars.Add(m.Groups[1].Value);
            }

            if (paramVars.Count == 0)
                return;

            var guardedVars = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in ReadOnlyGuardRegex.Matches(code))
            {
                string v1 = m.Groups.Count > 1 ? m.Groups[1].Value : string.Empty;
                string v2 = m.Groups.Count > 2 ? m.Groups[2].Value : string.Empty;
                if (!string.IsNullOrWhiteSpace(v1)) guardedVars.Add(v1);
                if (!string.IsNullOrWhiteSpace(v2)) guardedVars.Add(v2);
            }

            foreach (Match m in ReadOnlySetRegex.Matches(code))
            {
                if (m.Groups.Count <= 1)
                    continue;

                string varName = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(varName))
                    continue;
                if (!paramVars.Contains(varName))
                    continue;
                if (guardedVars.Contains(varName))
                    continue;

                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Category = "AccessConstraint",
                    Symbol = varName + ".Set",
                    Message = "Potential Parameter.Set call without IsReadOnly guard.",
                    Candidates = new List<string> { "if " + varName + " and not " + varName + ".IsReadOnly: " + varName + ".Set(...)" }
                });
            }
        }

        // #3: Known removed APIs that must be blocked (not just warned)
        private static readonly HashSet<string> RemovedTypes = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        private static readonly HashSet<string> RemovedMembers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Element.LevelId",
            "ElementId.IntegerValue",
            "Document.PlanTopologies",
        };

        private static void ValidateVersionCompatibility(
            IEnumerable<TypeMemberRef> typeMembers,
            IEnumerable<string> constructorTypeNames,
            Dictionary<string, string> typeAliasMap,
            RevitApiIndex apiIndex,
            RevitApiXmlIndex xmlIndex,
            List<ValidationIssue> issues)
        {
            foreach (var typeNameRaw in constructorTypeNames)
            {
                string typeName = ResolveTypeAlias(typeNameRaw, typeAliasMap);
                if (string.IsNullOrWhiteSpace(typeName))
                    continue;

                if (apiIndex.DeprecatedTypes.Contains(typeName))
                {
                    // #3: Removed types get Block, deprecated get Warning
                    var severity = RemovedTypes.Contains(typeName)
                        ? ValidationSeverity.Block
                        : ValidationSeverity.Warning;
                    AddIssue(issues, new ValidationIssue
                    {
                        Severity = severity,
                        Category = "VersionCompatibility",
                        Symbol = typeName,
                        Message = severity == ValidationSeverity.Block
                            ? "Type was REMOVED in Revit 2024+. Using it will cause runtime errors."
                            : "Type is marked obsolete in the current Revit runtime.",
                        Candidates = new List<string>()
                    });
                }

                if (xmlIndex != null && xmlIndex.DeprecatedTypes.Contains(typeName))
                {
                    AddIssue(issues, new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Category = "VersionCompatibility",
                        Symbol = typeName,
                        Message = "Type is marked deprecated in RevitAPI.xml documentation.",
                        Candidates = new List<string>()
                    });
                }
            }

            foreach (var memberRef in typeMembers)
            {
                string typeName = ResolveTypeAlias(memberRef.TypeName, typeAliasMap);
                if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(memberRef.MemberName))
                    continue;

                string memberKey = typeName + "." + memberRef.MemberName;

                if (apiIndex.DeprecatedMembers.Contains(memberKey))
                {
                    // #3: Removed members get Block, deprecated get Warning
                    var severity = RemovedMembers.Contains(memberKey)
                        ? ValidationSeverity.Block
                        : ValidationSeverity.Warning;
                    AddIssue(issues, new ValidationIssue
                    {
                        Severity = severity,
                        Category = "VersionCompatibility",
                        Symbol = memberKey,
                        Message = severity == ValidationSeverity.Block
                            ? "Member was REMOVED in Revit 2024+. Using it will cause runtime errors."
                            : "Member is marked obsolete in the current Revit runtime.",
                        Candidates = new List<string>()
                    });
                }

                if (xmlIndex != null && xmlIndex.DeprecatedMembers.Contains(memberKey))
                {
                    string hint = null;
                    if (xmlIndex.MemberSummaries.ContainsKey(memberKey))
                        hint = xmlIndex.MemberSummaries[memberKey];

                    AddIssue(issues, new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Category = "VersionCompatibility",
                        Symbol = memberKey,
                        Message = string.IsNullOrWhiteSpace(hint)
                            ? "Member is marked deprecated in RevitAPI.xml documentation."
                            : "Member is marked deprecated in RevitAPI.xml. " + hint,
                        Candidates = new List<string>()
                    });
                }
            }
        }

        private static void ValidatePythonRuntimeRules(string code, string revitVersion, List<ValidationIssue> issues)
        {
            bool isIronPython = string.Equals(revitVersion, "2022", StringComparison.Ordinal);
            if (isIronPython)
                return;

            if (CpythonExceptRegex.IsMatch(code))
            {
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "Runtime",
                    Symbol = "except Exception, e",
                    Message = "IronPython-only exception syntax is not valid in CPython 3.",
                    Candidates = new List<string> { "except Exception as e:" }
                });
            }

            if (code.IndexOf("xrange(", StringComparison.Ordinal) >= 0)
            {
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "Runtime",
                    Symbol = "xrange",
                    Message = "xrange is not available in CPython 3.",
                    Candidates = new List<string> { "range" }
                });
            }

            if (code.IndexOf("unicode(", StringComparison.Ordinal) >= 0)
            {
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "Runtime",
                    Symbol = "unicode",
                    Message = "unicode type is not available in CPython 3.",
                    Candidates = new List<string> { "str" }
                });
            }
        }

        private static void ValidateRevitUsageRules(string code, string revitVersion, List<ValidationIssue> issues)
        {
            if (code.IndexOf("raise SystemExit", StringComparison.Ordinal) >= 0 ||
                code.IndexOf("sys.exit(", StringComparison.Ordinal) >= 0 ||
                code.IndexOf("exit()", StringComparison.Ordinal) >= 0)
            {
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "RevitUsage",
                    Symbol = "SystemExit/exit",
                    Message = "Hard process exit is not allowed in Dynamo scripts.",
                    Candidates = new List<string> { "Return error through OUT" }
                });
            }

            if (code.IndexOf("__revit__", StringComparison.Ordinal) >= 0 ||
                code.IndexOf("uidoc.Selection", StringComparison.Ordinal) >= 0)
            {
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "RevitUsage",
                    Symbol = "__revit__/uidoc.Selection",
                    Message = "pyRevit UI pattern is not valid in Dynamo script runtime.",
                    Candidates = new List<string> { "Use Dynamo input nodes and IN[0]" }
                });
            }

            bool isIronPython = string.Equals(revitVersion, "2022", StringComparison.Ordinal);
            if (!isIronPython &&
                (code.IndexOf("AddReference('RevitAPIUI')", StringComparison.Ordinal) >= 0 ||
                 code.IndexOf("AddReference(\"RevitAPIUI\")", StringComparison.Ordinal) >= 0))
            {
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Block,
                    Category = "Runtime",
                    Symbol = "RevitAPIUI",
                    Message = "RevitAPIUI is blocked for CPython runtime in Dynamo.",
                    Candidates = new List<string> { "Use Dynamo selection nodes instead of UI API" }
                });
            }

            if (code.IndexOf("DocumentManager.Instance.CurrentDBDocument", StringComparison.Ordinal) < 0)
            {
                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Category = "RevitUsage",
                    Symbol = "DocumentManager",
                    Message = "CurrentDBDocument pattern was not detected.",
                    Candidates = new List<string> { "doc = DocumentManager.Instance.CurrentDBDocument" }
                });
            }

            // StorageType guard check: using AsDouble/AsString/AsInteger without StorageType check
            bool hasAsDouble   = code.IndexOf(".AsDouble()",   StringComparison.Ordinal) >= 0;
            bool hasAsString   = code.IndexOf(".AsString()",   StringComparison.Ordinal) >= 0;
            bool hasAsInteger  = code.IndexOf(".AsInteger()",  StringComparison.Ordinal) >= 0;
            bool hasStorageCheck = code.IndexOf("StorageType.", StringComparison.Ordinal) >= 0;

            if ((hasAsDouble || hasAsString || hasAsInteger) && !hasStorageCheck)
            {
                string usedMethods = string.Join(", ",
                    (hasAsDouble  ? new[] { "AsDouble()" }  : new string[0])
                    .Concat(hasAsString  ? new[] { "AsString()" }  : new string[0])
                    .Concat(hasAsInteger ? new[] { "AsInteger()" } : new string[0]));

                AddIssue(issues, new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Category = "ParameterAccess",
                    Symbol = usedMethods,
                    Message = "Parameter type accessor used without StorageType guard — will throw InvalidOperationException if type mismatches.",
                    Candidates = new List<string>
                    {
                        "if param and param.StorageType == StorageType.Double: value = param.AsDouble()",
                        "if param and param.StorageType == StorageType.String: value = param.AsString()",
                        "if param and param.StorageType == StorageType.Integer: value = param.AsInteger()"
                    }
                });
            }
        }

        private static string TryAutoFixEnums(string code, List<ValidationIssue> issues, RevitApiIndex index)
        {
            var enumIssues = issues
                .Where(i => i.Severity == ValidationSeverity.Block &&
                            (i.Category == "BuiltInParameter" ||
                             i.Category == "BuiltInCategory" ||
                             i.Category == "UnitTypeId"))
                .ToList();

            if (enumIssues.Count == 0)
                return code;

            var replacements = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var issue in enumIssues)
            {
                HashSet<string> targetSet = issue.Category == "BuiltInParameter"
                    ? index.BuiltInParameters
                    : issue.Category == "BuiltInCategory"
                        ? index.BuiltInCategories
                        : index.UnitTypeIds;

                string replacement = FindBestEnumReplacement(issue.Symbol, issue.Candidates, targetSet);
                if (!string.IsNullOrWhiteSpace(replacement) &&
                    !string.Equals(replacement, issue.Symbol, StringComparison.Ordinal))
                {
                    replacements[issue.Symbol] = replacement;
                }
            }

            if (replacements.Count == 0)
                return code;

            string fixedCode = code;
            foreach (var pair in replacements)
            {
                fixedCode = Regex.Replace(
                    fixedCode,
                    @"\b" + Regex.Escape(pair.Key) + @"\b",
                    pair.Value);
            }

            return fixedCode;
        }

        private static string TryAutoFixAccessConstraints(string code, List<ValidationIssue> issues)
        {
            var accessIssues = issues
                .Where(i => i.Severity == ValidationSeverity.Block &&
                            string.Equals(i.Category, "AccessConstraint", StringComparison.Ordinal) &&
                            !string.IsNullOrWhiteSpace(i.Symbol) &&
                            i.Symbol.Contains("."))
                .ToList();

            if (accessIssues.Count == 0)
                return code;

            var instanceMethodFixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ToString", "GetType", "GetHashCode", "Equals",
                "GetElement", "GetElements", "GetElementById"
            };

            string fixedCode = code;
            bool changed = false;

            foreach (var issue in accessIssues)
            {
                int dotIdx = issue.Symbol.IndexOf('.');
                if (dotIdx <= 0 || dotIdx >= issue.Symbol.Length - 1)
                    continue;

                string typeName = issue.Symbol.Substring(0, dotIdx);
                string memberName = issue.Symbol.Substring(dotIdx + 1);

                if (!instanceMethodFixes.Contains(memberName))
                    continue;

                if (string.Equals(typeName, "ViewType", StringComparison.Ordinal) &&
                    string.Equals(memberName, "ToString", StringComparison.Ordinal))
                {
                    var viewTypeComparePattern = new Regex(
                        @"ViewType\.ToString\s*\(\s*\)\s*==\s*[""'](\w+)[""']",
                        RegexOptions.Compiled);
                    fixedCode = viewTypeComparePattern.Replace(fixedCode, m =>
                        "active_view.ViewType == ViewType." + m.Groups[1].Value);

                    var viewTypeComparePatternNe = new Regex(
                        @"ViewType\.ToString\s*\(\s*\)\s*!=\s*[""'](\w+)[""']",
                        RegexOptions.Compiled);
                    fixedCode = viewTypeComparePatternNe.Replace(fixedCode, m =>
                        "active_view.ViewType != ViewType." + m.Groups[1].Value);

                    var viewTypeStandalone = new Regex(@"ViewType\.ToString\s*\(\s*\)", RegexOptions.Compiled);
                    fixedCode = viewTypeStandalone.Replace(fixedCode, "str(active_view.ViewType)");

                    changed = true;
                }
                else if (string.Equals(typeName, "Document", StringComparison.Ordinal) &&
                         (string.Equals(memberName, "GetElement", StringComparison.Ordinal) ||
                          string.Equals(memberName, "GetElements", StringComparison.Ordinal) ||
                          string.Equals(memberName, "GetElementById", StringComparison.Ordinal)))
                {
                    var docPattern = new Regex(
                        @"\bDocument\." + Regex.Escape(memberName) + @"\s*\(",
                        RegexOptions.Compiled);
                    string before = fixedCode;
                    fixedCode = docPattern.Replace(fixedCode, "doc." + memberName + "(");
                    if (!string.Equals(before, fixedCode, StringComparison.Ordinal))
                        changed = true;
                }
                else
                {
                    var genericPattern = new Regex(
                        @"\b" + Regex.Escape(typeName) + @"\." + Regex.Escape(memberName) + @"\s*\(\s*\)",
                        RegexOptions.Compiled);
                    string replacement = "# FIXME: " + typeName + "." + memberName + "() is an instance call — use an instance variable";
                    string before = fixedCode;
                    fixedCode = genericPattern.Replace(fixedCode, replacement);
                    if (!string.Equals(before, fixedCode, StringComparison.Ordinal))
                        changed = true;
                }
            }

            return changed ? fixedCode : code;
        }

        private static string FindBestEnumReplacement(string original, List<string> candidates, HashSet<string> targetSet)
        {
            if (targetSet == null || targetSet.Count == 0 || string.IsNullOrWhiteSpace(original))
                return null;

            string normalized = original;
            var directVariants = new List<string>
            {
                normalized.Replace("_NUM_OF_", "_NUMBER_OF_"),
                normalized.Replace("_NUM_", "_NUMBER_"),
                normalized.Replace("_NUMBER_OF_", "_NUM_"),
                normalized.Replace("_INTEGER", "_INT"),
                normalized.Replace("_INT", "_INTEGER")
            };

            foreach (var variant in directVariants)
            {
                if (!string.IsNullOrWhiteSpace(variant) && targetSet.Contains(variant))
                    return variant;
            }

            if (candidates != null && candidates.Count > 0)
            {
                string top = candidates[0];
                if (ComputeLevenshteinDistance(original, top) <= 5)
                    return top;
            }

            return null;
        }

        private static string NormalizeRolloutPhase(string phase)
        {
            if (string.IsNullOrWhiteSpace(phase))
                return "phase3";

            string normalized = phase.Trim().ToLowerInvariant();
            if (normalized == "phase1" || normalized == "1")
                return "phase1";
            if (normalized == "phase2" || normalized == "2")
                return "phase2";

            return "phase3";
        }

        private static EnumDebugSnapshot BuildEnumDebugSnapshot(SymbolExtractionResult extracted, RevitApiIndex apiIndex)
        {
            var referencedBip = new HashSet<string>(StringComparer.Ordinal);
            var referencedBic = new HashSet<string>(StringComparer.Ordinal);
            var referencedUnit = new HashSet<string>(StringComparer.Ordinal);

            if (extracted != null)
            {
                if (extracted.BuiltInParameters != null)
                {
                    foreach (var symbol in extracted.BuiltInParameters)
                    {
                        if (!string.IsNullOrWhiteSpace(symbol))
                            referencedBip.Add(symbol);
                    }
                }

                if (extracted.BuiltInCategories != null)
                {
                    foreach (var symbol in extracted.BuiltInCategories)
                    {
                        if (!string.IsNullOrWhiteSpace(symbol))
                            referencedBic.Add(symbol);
                    }
                }

                if (extracted.UnitTypeIds != null)
                {
                    foreach (var symbol in extracted.UnitTypeIds)
                    {
                        if (!string.IsNullOrWhiteSpace(symbol))
                            referencedUnit.Add(symbol);
                    }
                }

                if (extracted.TypeMemberRefs != null)
                {
                    foreach (var memberRef in extracted.TypeMemberRefs)
                    {
                        if (memberRef == null || string.IsNullOrWhiteSpace(memberRef.MemberName))
                            continue;

                        string resolvedType = ResolveTypeAlias(memberRef.TypeName, extracted.TypeAliasMap);
                        if (string.Equals(resolvedType, "BuiltInParameter", StringComparison.Ordinal))
                        {
                            referencedBip.Add(memberRef.MemberName);
                        }
                        else if (string.Equals(resolvedType, "BuiltInCategory", StringComparison.Ordinal))
                        {
                            referencedBic.Add(memberRef.MemberName);
                        }
                        else if (string.Equals(resolvedType, "UnitTypeId", StringComparison.Ordinal))
                        {
                            referencedUnit.Add(memberRef.MemberName);
                        }
                    }
                }
            }

            bool indexReady = apiIndex != null && apiIndex.IsReady;
            var missingBip = new List<string>();
            var missingBic = new List<string>();
            var missingUnit = new List<string>();

            if (indexReady)
            {
                foreach (var symbol in referencedBip)
                {
                    if (!apiIndex.BuiltInParameters.Contains(symbol))
                        missingBip.Add(symbol);
                }

                foreach (var symbol in referencedBic)
                {
                    if (!apiIndex.BuiltInCategories.Contains(symbol))
                        missingBic.Add(symbol);
                }

                foreach (var symbol in referencedUnit)
                {
                    if (!apiIndex.UnitTypeIds.Contains(symbol))
                        missingUnit.Add(symbol);
                }
            }

            return new EnumDebugSnapshot
            {
                ReferencedBuiltInParameters = referencedBip.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ReferencedBuiltInCategories = referencedBic.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ReferencedUnitTypeIds = referencedUnit.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                MissingBuiltInParameters = missingBip.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                MissingBuiltInCategories = missingBic.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                MissingUnitTypeIds = missingUnit.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };
        }

        private static void AddIssue(List<ValidationIssue> issues, ValidationIssue issue)
        {
            bool exists = issues.Any(i =>
                i.Severity == issue.Severity &&
                string.Equals(i.Category, issue.Category, StringComparison.Ordinal) &&
                string.Equals(i.Symbol, issue.Symbol, StringComparison.Ordinal) &&
                string.Equals(i.Message, issue.Message, StringComparison.Ordinal));

            if (!exists)
            {
                issues.Add(issue);
            }
        }

        private static bool IsEnumTypeName(string typeName)
        {
            return string.Equals(typeName, "BuiltInParameter", StringComparison.Ordinal) ||
                   string.Equals(typeName, "BuiltInCategory", StringComparison.Ordinal) ||
                   string.Equals(typeName, "UnitTypeId", StringComparison.Ordinal);
        }

        private static string ResolveTypeAlias(string typeName, Dictionary<string, string> typeAliasMap)
        {
            if (string.IsNullOrWhiteSpace(typeName) || typeAliasMap == null)
                return typeName;

            string resolved;
            if (typeAliasMap.TryGetValue(typeName, out resolved) && !string.IsNullOrWhiteSpace(resolved))
                return resolved;

            return typeName;
        }

        private static bool IsExplicitRevitTypeReference(
            string originalTypeName,
            Dictionary<string, string> importModules,
            Dictionary<string, string> typeAliasMap)
        {
            if (string.IsNullOrWhiteSpace(originalTypeName) || importModules == null)
                return false;

            string module;
            if (!importModules.TryGetValue(originalTypeName, out module))
                return false;

            if (!module.StartsWith("Autodesk.Revit", StringComparison.Ordinal))
                return false;

            if (typeAliasMap == null)
                return false;

            return typeAliasMap.ContainsKey(originalTypeName);
        }

        private static bool ShouldSkipByImport(
            string typeName,
            Dictionary<string, string> importModules)
        {
            if (importModules == null)
                return false;

            string module;
            if (importModules.TryGetValue(typeName, out module))
            {
                if (!module.StartsWith("Autodesk.Revit", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsModuleAlias(
            string typeName,
            Dictionary<string, string> importModules,
            Dictionary<string, string> typeAliasMap)
        {
            if (string.IsNullOrWhiteSpace(typeName) || importModules == null)
                return false;

            string module;
            if (!importModules.TryGetValue(typeName, out module))
                return false;

            if (!module.StartsWith("Autodesk.Revit", StringComparison.Ordinal))
                return false;

            if (typeAliasMap != null && typeAliasMap.ContainsKey(typeName))
                return false;

            return true;
        }

        private static bool ShouldCheckAsRevitType(
            string typeName,
            Dictionary<string, string> importModules,
            Dictionary<string, string> typeAliasMap)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            if (NonRevitTypeAllowList.Contains(typeName))
                return false;

            // #10: .NET types like List/Dictionary skip Revit index but are still tracked
            if (DotNetTypeAllowList.Contains(typeName))
                return false;

            if (ShouldSkipByImport(typeName, importModules))
                return false;

            if (IsModuleAlias(typeName, importModules, typeAliasMap))
                return false;

            return true;
        }

        private static bool IsLikelyLocalIdentifier(string token, HashSet<string> localIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(token) || localIdentifiers == null || localIdentifiers.Count == 0)
                return false;

            return localIdentifiers.Contains(token);
        }

        private static List<string> SuggestCandidates(string symbol, IEnumerable<string> corpus, int limit)
        {
            if (string.IsNullOrWhiteSpace(symbol) || corpus == null)
                return new List<string>();

            var ranked = new List<Tuple<string, int>>();
            foreach (var candidate in corpus)
            {
                int distance = ComputeLevenshteinDistance(symbol, candidate);
                ranked.Add(Tuple.Create(candidate, distance));
            }

            return ranked
                .OrderBy(t => t.Item2)
                .ThenBy(t => t.Item1, StringComparer.Ordinal)
                .Take(limit)
                .Select(t => t.Item1)
                .ToList();
        }

        private static List<string> BuildSignatureCandidates(IEnumerable<CallableSignature> signatures, int limit)
        {
            return signatures
                .Distinct(new CallableSignatureComparer())
                .Select(s => s.ToText())
                .Take(limit)
                .ToList();
        }

        private static int CountTopLevelArguments(string argumentsText)
        {
            if (argumentsText == null)
                return -1;

            string text = argumentsText.Trim();
            if (text.Length == 0)
                return 0;

            int count = 1;
            int depthParen = 0;
            int depthBracket = 0;
            int depthBrace = 0;
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool escaping = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (!inDoubleQuote && ch == '\'')
                {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (!inSingleQuote && ch == '"')
                {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }

                if (inSingleQuote || inDoubleQuote)
                    continue;

                if (ch == '(') depthParen++;
                else if (ch == ')' && depthParen > 0) depthParen--;
                else if (ch == '[') depthBracket++;
                else if (ch == ']' && depthBracket > 0) depthBracket--;
                else if (ch == '{') depthBrace++;
                else if (ch == '}' && depthBrace > 0) depthBrace--;
                else if (ch == ',' && depthParen == 0 && depthBracket == 0 && depthBrace == 0)
                    count++;
            }

            return count;
        }

        private static int ComputeLevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
                return string.IsNullOrEmpty(b) ? 0 : b.Length;
            if (string.IsNullOrEmpty(b))
                return a.Length;

            int[,] dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }

            return dp[a.Length, b.Length];
        }

        private sealed class SymbolExtractionResult
        {
            public List<string> BuiltInParameters { get; set; }
            public List<string> BuiltInCategories { get; set; }
            public List<string> UnitTypeIds { get; set; }
            public List<TypeMemberRef> TypeMemberRefs { get; set; }
            public List<TypeMethodCallRef> TypeMethodCallRefs { get; set; }
            public List<string> TypeConstructorRefs { get; set; }
            public List<TypeConstructorCallRef> TypeConstructorCallRefs { get; set; }
            public Dictionary<string, string> ImportModules { get; set; }
            public Dictionary<string, string> TypeAliasMap { get; set; }
            public HashSet<string> LocalIdentifiers { get; set; }

            public static SymbolExtractionResult Extract(string code)
            {
                var imports = ParseImportMaps(code);
                var localIdentifiers = ParseLocalIdentifiers(code);
                var result = new SymbolExtractionResult
                {
                    BuiltInParameters = BuiltInParameterRegex.Matches(code).Cast<Match>().Select(m => m.Groups[1].Value).ToList(),
                    BuiltInCategories = BuiltInCategoryRegex.Matches(code).Cast<Match>().Select(m => m.Groups[1].Value).ToList(),
                    UnitTypeIds = UnitTypeIdRegex.Matches(code).Cast<Match>().Select(m => m.Groups[1].Value).ToList(),
                    TypeMemberRefs = new List<TypeMemberRef>(),
                    TypeMethodCallRefs = new List<TypeMethodCallRef>(),
                    TypeConstructorRefs = new List<string>(),
                    TypeConstructorCallRefs = new List<TypeConstructorCallRef>(),
                    ImportModules = imports.ImportModules,
                    TypeAliasMap = imports.TypeAliases,
                    LocalIdentifiers = localIdentifiers
                };

                foreach (Match match in TypeMemberRegex.Matches(code))
                {
                    string left = match.Groups[1].Value;
                    string right = match.Groups[2].Value;
                    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                        continue;

                    result.TypeMemberRefs.Add(new TypeMemberRef
                    {
                        TypeName = left,
                        MemberName = right
                    });
                }

                foreach (Match match in TypeMethodCallRegex.Matches(code))
                {
                    string typeName = match.Groups[1].Value;
                    string methodName = match.Groups[2].Value;
                    if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(methodName))
                        continue;

                    // #7: Extract arguments from full match using parenthesis counter
                    string fullMatch = match.Value;
                    int parenStart = fullMatch.IndexOf('(', fullMatch.IndexOf(methodName));
                    string argsText = "";
                    if (parenStart >= 0 && parenStart < fullMatch.Length - 1)
                    {
                        argsText = fullMatch.Substring(parenStart + 1, fullMatch.Length - parenStart - 2);
                    }

                    result.TypeMethodCallRefs.Add(new TypeMethodCallRef
                    {
                        TypeName = typeName,
                        MethodName = methodName,
                        ArgumentCount = CountTopLevelArguments(argsText)
                    });
                }

                foreach (Match match in TypeCtorRegex.Matches(code))
                {
                    string token = match.Groups[1].Value;
                    if (token == "True" || token == "False" || token == "None")
                        continue;
                    result.TypeConstructorRefs.Add(token);
                }

                foreach (Match match in TypeCtorCallRegex.Matches(code))
                {
                    string token = match.Groups[1].Value;
                    if (token == "True" || token == "False" || token == "None")
                        continue;

                    // #7: Extract arguments from full match using parenthesis counter
                    string fullMatch = match.Value;
                    int parenStart = fullMatch.IndexOf('(');
                    string argsText = "";
                    if (parenStart >= 0 && parenStart < fullMatch.Length - 1)
                    {
                        argsText = fullMatch.Substring(parenStart + 1, fullMatch.Length - parenStart - 2);
                    }

                    result.TypeConstructorCallRefs.Add(new TypeConstructorCallRef
                    {
                        TypeName = token,
                        ArgumentCount = CountTopLevelArguments(argsText)
                    });
                }

                return result;
            }

            public int GetTotalSymbolCount()
            {
                int total = 0;
                total += BuiltInParameters == null ? 0 : BuiltInParameters.Count;
                total += BuiltInCategories == null ? 0 : BuiltInCategories.Count;
                total += UnitTypeIds == null ? 0 : UnitTypeIds.Count;
                total += TypeMemberRefs == null ? 0 : TypeMemberRefs.Count;
                total += TypeMethodCallRefs == null ? 0 : TypeMethodCallRefs.Count;
                total += TypeConstructorRefs == null ? 0 : TypeConstructorRefs.Count;
                total += TypeConstructorCallRefs == null ? 0 : TypeConstructorCallRefs.Count;
                return total;
            }

            private static ImportParseResult ParseImportMaps(string code)
            {
                var result = new ImportParseResult
                {
                    ImportModules = new Dictionary<string, string>(StringComparer.Ordinal),
                    TypeAliases = new Dictionary<string, string>(StringComparer.Ordinal)
                };

                var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var raw in lines)
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    var fromMatch = ImportFromRegex.Match(line);
                    if (fromMatch.Success)
                    {
                        string module = fromMatch.Groups[1].Value.Trim();
                        string names = fromMatch.Groups[2].Value.Trim();

                        foreach (string partRaw in names.Split(','))
                        {
                            string part = partRaw.Trim();
                            if (part.Length == 0 || part == "*")
                                continue;

                            var tokens = part.Split(new[] { " as " }, StringSplitOptions.None);
                            string symbol = tokens[0].Trim();
                            string alias = tokens.Length > 1 ? tokens[1].Trim() : symbol;
                            if (alias.Length == 0)
                                continue;

                            result.ImportModules[alias] = module;
                            if (module.StartsWith("Autodesk.Revit", StringComparison.Ordinal) &&
                                symbol.Length > 0 &&
                                char.IsUpper(symbol[0]))
                            {
                                result.TypeAliases[alias] = symbol;
                            }
                        }

                        continue;
                    }

                    var importMatch = ImportRegex.Match(line);
                    if (importMatch.Success)
                    {
                        string module = importMatch.Groups[1].Value.Trim();
                        string alias = importMatch.Groups[2].Success
                            ? importMatch.Groups[2].Value.Trim()
                            : module.Split('.')[0];

                        if (alias.Length > 0)
                            result.ImportModules[alias] = module;
                    }
                }

                return result;
            }

            private static HashSet<string> ParseLocalIdentifiers(string code)
            {
                var identifiers = new HashSet<string>(StringComparer.Ordinal);
                if (string.IsNullOrWhiteSpace(code))
                    return identifiers;

                var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    var assign = LocalAssignRegex.Match(line);
                    if (assign.Success && assign.Groups.Count > 1)
                    {
                        string name = assign.Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(name))
                            identifiers.Add(name);
                    }

                    var forVar = LocalForVarRegex.Match(line);
                    if (forVar.Success && forVar.Groups.Count > 1)
                    {
                        string name = forVar.Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(name))
                            identifiers.Add(name);
                    }
                }

                return identifiers;
            }
        }

        private sealed class EnumDebugSnapshot
        {
            public List<string> ReferencedBuiltInParameters { get; set; }
            public List<string> ReferencedBuiltInCategories { get; set; }
            public List<string> ReferencedUnitTypeIds { get; set; }
            public List<string> MissingBuiltInParameters { get; set; }
            public List<string> MissingBuiltInCategories { get; set; }
            public List<string> MissingUnitTypeIds { get; set; }
        }

        private sealed class ImportParseResult
        {
            public Dictionary<string, string> ImportModules { get; set; }
            public Dictionary<string, string> TypeAliases { get; set; }
        }

        private sealed class TypeMemberRef
        {
            public string TypeName { get; set; }
            public string MemberName { get; set; }
        }

        private sealed class TypeMethodCallRef
        {
            public string TypeName { get; set; }
            public string MethodName { get; set; }
            public int ArgumentCount { get; set; }
        }

        private sealed class TypeConstructorCallRef
        {
            public string TypeName { get; set; }
            public int ArgumentCount { get; set; }
        }

        private sealed class CallableSignature
        {
            public int MinArgs { get; set; }
            public int MaxArgs { get; set; }
            public bool HasParamArray { get; set; }

            public bool Accepts(int count)
            {
                if (count < MinArgs)
                    return false;
                if (HasParamArray)
                    return true;
                return count <= MaxArgs;
            }

            public string ToText()
            {
                if (HasParamArray)
                    return MinArgs + "+";

                if (MinArgs == MaxArgs)
                    return MinArgs.ToString();

                return MinArgs + "-" + MaxArgs;
            }
        }

        private sealed class CallableSignatureComparer : IEqualityComparer<CallableSignature>
        {
            public bool Equals(CallableSignature x, CallableSignature y)
            {
                if (ReferenceEquals(x, y))
                    return true;
                if (x == null || y == null)
                    return false;

                return x.MinArgs == y.MinArgs &&
                       x.MaxArgs == y.MaxArgs &&
                       x.HasParamArray == y.HasParamArray;
            }

            public int GetHashCode(CallableSignature obj)
            {
                if (obj == null)
                    return 0;

                unchecked
                {
                    int hash = 17;
                    hash = (hash * 23) + obj.MinArgs.GetHashCode();
                    hash = (hash * 23) + obj.MaxArgs.GetHashCode();
                    hash = (hash * 23) + obj.HasParamArray.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class RevitApiIndex
        {
            public bool IsReady { get; set; }
            public string Status { get; set; }
            public string AssemblyIdentity { get; set; }
            public HashSet<string> BuiltInParameters { get; set; }
            public HashSet<string> BuiltInCategories { get; set; }
            public HashSet<string> UnitTypeIds { get; set; }
            public HashSet<string> TypeNames { get; set; }
            public Dictionary<string, HashSet<string>> TypeMembers { get; set; }
            public Dictionary<string, HashSet<string>> TypeStaticMembers { get; set; }
            public Dictionary<string, HashSet<string>> TypeInstanceMembers { get; set; }
            public Dictionary<string, Dictionary<string, List<CallableSignature>>> TypeMethodSignatures { get; set; }
            public Dictionary<string, List<CallableSignature>> TypeConstructorSignatures { get; set; }
            public HashSet<string> DeprecatedTypes { get; set; }
            public HashSet<string> DeprecatedMembers { get; set; }
        }

        private static class RevitApiIndexCache
        {
            private static readonly object LockObject = new object();
            private static RevitApiIndex _cache;

            public static RevitApiIndex GetOrCreate()
            {
                lock (LockObject)
                {
                    var assembly = FindRevitApiAssembly();
                    if (assembly == null)
                    {
                        return new RevitApiIndex
                        {
                            IsReady = false,
                            Status = "revitapi_not_loaded",
                            AssemblyIdentity = "none",
                            BuiltInParameters = new HashSet<string>(StringComparer.Ordinal),
                            BuiltInCategories = new HashSet<string>(StringComparer.Ordinal),
                            UnitTypeIds = new HashSet<string>(StringComparer.Ordinal),
                            TypeNames = new HashSet<string>(StringComparer.Ordinal),
                            TypeMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                            TypeStaticMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                            TypeInstanceMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                            TypeMethodSignatures = new Dictionary<string, Dictionary<string, List<CallableSignature>>>(StringComparer.Ordinal),
                            TypeConstructorSignatures = new Dictionary<string, List<CallableSignature>>(StringComparer.Ordinal),
                            DeprecatedTypes = new HashSet<string>(StringComparer.Ordinal),
                            DeprecatedMembers = new HashSet<string>(StringComparer.Ordinal)
                        };
                    }

                    string identity = assembly.FullName ?? assembly.GetName().Name;
                    if (_cache != null && string.Equals(_cache.AssemblyIdentity, identity, StringComparison.Ordinal))
                    {
                        return _cache;
                    }

                    _cache = BuildIndex(assembly, identity);
                    return _cache;
                }
            }

            private static RevitApiIndex BuildIndex(Assembly assembly, string identity)
            {
                var index = new RevitApiIndex
                {
                    IsReady = true,
                    Status = "ok",
                    AssemblyIdentity = identity,
                    BuiltInParameters = new HashSet<string>(StringComparer.Ordinal),
                    BuiltInCategories = new HashSet<string>(StringComparer.Ordinal),
                    UnitTypeIds = new HashSet<string>(StringComparer.Ordinal),
                    TypeNames = new HashSet<string>(StringComparer.Ordinal),
                    TypeMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                    TypeStaticMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                    TypeInstanceMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                    TypeMethodSignatures = new Dictionary<string, Dictionary<string, List<CallableSignature>>>(StringComparer.Ordinal),
                    TypeConstructorSignatures = new Dictionary<string, List<CallableSignature>>(StringComparer.Ordinal),
                    DeprecatedTypes = new HashSet<string>(StringComparer.Ordinal),
                    DeprecatedMembers = new HashSet<string>(StringComparer.Ordinal)
                };

                try
                {
                    Type bip = assembly.GetType("Autodesk.Revit.DB.BuiltInParameter");
                    Type bic = assembly.GetType("Autodesk.Revit.DB.BuiltInCategory");
                    Type unitTypeId = assembly.GetType("Autodesk.Revit.DB.UnitTypeId");

                    if (bip != null && bip.IsEnum)
                    {
                        foreach (var name in Enum.GetNames(bip))
                            index.BuiltInParameters.Add(name);
                    }

                    if (bic != null && bic.IsEnum)
                    {
                        foreach (var name in Enum.GetNames(bic))
                            index.BuiltInCategories.Add(name);
                    }

                    if (unitTypeId != null)
                    {
                        foreach (var prop in unitTypeId.GetProperties(BindingFlags.Public | BindingFlags.Static))
                        {
                            index.UnitTypeIds.Add(prop.Name);
                        }
                    }

                    foreach (var type in SafeGetTypes(assembly))
                    {
                        if (type == null || !type.IsPublic || string.IsNullOrWhiteSpace(type.Name))
                            continue;
                        if (type.Namespace == null || !type.Namespace.StartsWith("Autodesk.Revit.DB", StringComparison.Ordinal))
                            continue;

                        index.TypeNames.Add(type.Name);

                        HashSet<string> members;
                        if (!index.TypeMembers.TryGetValue(type.Name, out members))
                        {
                            members = new HashSet<string>(StringComparer.Ordinal);
                            index.TypeMembers[type.Name] = members;
                        }

                        HashSet<string> staticMembers;
                        if (!index.TypeStaticMembers.TryGetValue(type.Name, out staticMembers))
                        {
                            staticMembers = new HashSet<string>(StringComparer.Ordinal);
                            index.TypeStaticMembers[type.Name] = staticMembers;
                        }

                        HashSet<string> instanceMembers;
                        if (!index.TypeInstanceMembers.TryGetValue(type.Name, out instanceMembers))
                        {
                            instanceMembers = new HashSet<string>(StringComparer.Ordinal);
                            index.TypeInstanceMembers[type.Name] = instanceMembers;
                        }

                        Dictionary<string, List<CallableSignature>> methodSignatures;
                        if (!index.TypeMethodSignatures.TryGetValue(type.Name, out methodSignatures))
                        {
                            methodSignatures = new Dictionary<string, List<CallableSignature>>(StringComparer.Ordinal);
                            index.TypeMethodSignatures[type.Name] = methodSignatures;
                        }

                        var constructorSignatures = new List<CallableSignature>();
                        index.TypeConstructorSignatures[type.Name] = constructorSignatures;

                        if (type.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0)
                            index.DeprecatedTypes.Add(type.Name);

                        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                        {
                            var sig = CreateSignature(ctor.GetParameters());
                            AddSignature(constructorSignatures, sig);
                        }

                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                        {
                            if (!method.IsSpecialName)
                            {
                                members.Add(method.Name);
                                if (method.IsStatic) staticMembers.Add(method.Name);
                                else instanceMembers.Add(method.Name);
                                var sig = CreateSignature(method.GetParameters());
                                AddMethodSignature(methodSignatures, method.Name, sig);

                                if (method.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0)
                                    index.DeprecatedMembers.Add(type.Name + "." + method.Name);
                            }
                        }

                        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                        {
                            members.Add(property.Name);
                            var accessor = property.GetGetMethod() ?? property.GetSetMethod();
                            bool isStatic = accessor != null && accessor.IsStatic;
                            if (isStatic) staticMembers.Add(property.Name);
                            else instanceMembers.Add(property.Name);

                            if (property.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0)
                                index.DeprecatedMembers.Add(type.Name + "." + property.Name);
                        }

                        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                        {
                            members.Add(field.Name);
                            if (field.IsStatic) staticMembers.Add(field.Name);
                            else instanceMembers.Add(field.Name);

                            if (field.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0)
                                index.DeprecatedMembers.Add(type.Name + "." + field.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    index.IsReady = false;
                    index.Status = "index_build_failed:" + ex.GetType().Name;
                }

                // #11: Merge Dynamo assembly types into the index
                try
                {
                    foreach (var dynamoAsm in FindDynamoAssemblies())
                    {
                        foreach (var type in SafeGetTypes(dynamoAsm))
                        {
                            if (type == null || !type.IsPublic || string.IsNullOrWhiteSpace(type.Name))
                                continue;

                            if (!index.TypeNames.Contains(type.Name))
                                index.TypeNames.Add(type.Name);

                            HashSet<string> members;
                            if (!index.TypeMembers.TryGetValue(type.Name, out members))
                            {
                                members = new HashSet<string>(StringComparer.Ordinal);
                                index.TypeMembers[type.Name] = members;
                            }

                            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                            {
                                if (!method.IsSpecialName)
                                    members.Add(method.Name);
                            }

                            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                            {
                                members.Add(property.Name);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("LocalCodeValidationService", $"Dynamo assembly indexing failed: {ex.GetType().Name}");
                }

                return index;
            }

            private static CallableSignature CreateSignature(ParameterInfo[] parameters)
            {
                int min = 0;
                int max = 0;
                bool hasParamArray = false;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    bool isParamArray = parameter.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0;

                    if (isParamArray)
                    {
                        hasParamArray = true;
                        max = int.MaxValue;
                        continue;
                    }

                    max++;

                    bool isOptional = parameter.IsOptional || parameter.HasDefaultValue;
                    if (!isOptional)
                        min++;
                }

                return new CallableSignature
                {
                    MinArgs = min,
                    MaxArgs = max,
                    HasParamArray = hasParamArray
                };
            }

            private static void AddMethodSignature(
                Dictionary<string, List<CallableSignature>> methodSignatures,
                string methodName,
                CallableSignature signature)
            {
                List<CallableSignature> bucket;
                if (!methodSignatures.TryGetValue(methodName, out bucket))
                {
                    bucket = new List<CallableSignature>();
                    methodSignatures[methodName] = bucket;
                }

                AddSignature(bucket, signature);
            }

            private static void AddSignature(List<CallableSignature> bucket, CallableSignature signature)
            {
                bool exists = bucket.Any(s =>
                    s.MinArgs == signature.MinArgs &&
                    s.MaxArgs == signature.MaxArgs &&
                    s.HasParamArray == signature.HasParamArray);

                if (!exists)
                {
                    bucket.Add(signature);
                }
            }

            private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(t => t != null);
                }
                catch
                {
                    return new Type[0];
                }
            }

            private static Assembly FindRevitApiAssembly()
            {
                try
                {
                    return AppDomain.CurrentDomain
                        .GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, "RevitAPI", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return null;
                }
            }

            // #11: Find Dynamo assemblies for extended validation coverage
            private static readonly string[] DynamoAssemblyNames = new[]
            {
                "RevitServices",
                "ProtoGeometry",
                "DSCoreNodes"
            };

            private static List<Assembly> FindDynamoAssemblies()
            {
                var result = new List<Assembly>();
                try
                {
                    var loaded = AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var name in DynamoAssemblyNames)
                    {
                        var asm = loaded.FirstOrDefault(a =>
                            string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
                        if (asm != null)
                            result.Add(asm);
                    }
                }
                catch
                {
                    // Ignore - Dynamo assemblies are optional
                }
                return result;
            }
        }
    }
}
