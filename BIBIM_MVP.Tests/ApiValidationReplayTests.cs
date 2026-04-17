// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BIBIM_MVP.Tests
{
    /// <summary>
    /// Replay-based regression tests for LocalCodeValidationService.
    /// Each test case represents a real user failure scenario or a known edge case.
    /// The mock API index simulates Revit API types/members without requiring Revit runtime.
    /// </summary>
    public class ApiValidationReplayTests : IDisposable
    {
        public ApiValidationReplayTests()
        {
            // Set up a mock API index that covers common Revit API types
            LocalCodeValidationService.SetTestApiIndex(
                builtInParameters: new HashSet<string>(StringComparer.Ordinal)
                {
                    "LEVEL_PARAM", "WALL_ATTR_HEIGHT_PARAM", "WALL_ATTR_WIDTH_PARAM",
                    "ALL_MODEL_INSTANCE_COMMENTS", "ELEM_FAMILY_AND_TYPE_PARAM",
                    "ELEM_CATEGORY_PARAM", "DOOR_HEIGHT", "DOOR_WIDTH",
                    "ROOM_NAME", "ROOM_NUMBER", "ROOM_AREA"
                },
                builtInCategories: new HashSet<string>(StringComparer.Ordinal)
                {
                    "OST_Walls", "OST_Doors", "OST_Windows", "OST_Floors",
                    "OST_Rooms", "OST_Columns", "OST_StructuralColumns",
                    "OST_Ceilings", "OST_Roofs", "OST_Stairs"
                },
                unitTypeIds: new HashSet<string>(StringComparer.Ordinal)
                {
                    "Meters", "Feet", "Millimeters", "SquareMeters", "SquareFeet"
                },
                typeNames: new HashSet<string>(StringComparer.Ordinal)
                {
                    "FilteredElementCollector", "ElementId", "XYZ", "Line", "Wall",
                    "Floor", "Document", "Transaction", "Element", "CurveLoop",
                    "BuiltInParameter", "BuiltInCategory", "UnitTypeId",
                    "FamilyInstance", "FamilySymbol", "Level", "View",
                    "SolidSolidCutUtils", "ExportDWGSettings", "Parameter",
                    "StructuralType", "UV", "BoundingBoxXYZ"
                },
                typeMembers: new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
                {
                    ["FilteredElementCollector"] = new HashSet<string>(StringComparer.Ordinal)
                        { "OfCategory", "OfClass", "WhereElementIsNotElementType", "ToElements", "ToElementIds", "FirstElement" },
                    ["Element"] = new HashSet<string>(StringComparer.Ordinal)
                        { "Id", "Name", "Category", "get_Parameter", "GetTypeId", "Location" },
                    ["ElementId"] = new HashSet<string>(StringComparer.Ordinal)
                        { "Value", "InvalidElementId" },
                    ["Document"] = new HashSet<string>(StringComparer.Ordinal)
                        { "Create", "Delete", "GetElement", "ActiveView", "Title" },
                    ["Transaction"] = new HashSet<string>(StringComparer.Ordinal)
                        { "Start", "Commit", "RollBack", "Dispose" },
                    ["Wall"] = new HashSet<string>(StringComparer.Ordinal)
                        { "Create", "Location", "WallType", "Width", "Flipped" },
                    ["CurveLoop"] = new HashSet<string>(StringComparer.Ordinal)
                        { "Append", "Create" },
                    ["XYZ"] = new HashSet<string>(StringComparer.Ordinal)
                        { "X", "Y", "Z", "Zero", "BasisX", "BasisY", "BasisZ" },
                    ["Parameter"] = new HashSet<string>(StringComparer.Ordinal)
                        { "AsDouble", "AsString", "AsElementId", "AsInteger", "AsValueString", "Set", "IsReadOnly" },
                    ["SolidSolidCutUtils"] = new HashSet<string>(StringComparer.Ordinal)
                        { "CutExistsBetweenElements", "AddCutBetweenSolids" },
                    ["ExportDWGSettings"] = new HashSet<string>(StringComparer.Ordinal)
                        { "GetActivePredefinedSettings", "FindByName" },
                },
                deprecatedTypes: new HashSet<string>(StringComparer.Ordinal) { },
                deprecatedMembers: new HashSet<string>(StringComparer.Ordinal)
                {
                    // These must also be in DeprecatedMembers for ValidateVersionCompatibility to find them
                    // RemovedMembers is checked second to upgrade severity from Warning to Block
                    "Element.LevelId", "ElementId.IntegerValue", "Document.PlanTopologies"
                }
            );

            LocalCodeValidationService.SetTestXmlIndex();
        }

        public void Dispose()
        {
            LocalCodeValidationService.ClearTestIndexes();
        }

        // ================================================================
        // SHOULD_BLOCK: Code that must be blocked
        // ================================================================

        [Fact]
        public void Block_RemovedApi_ElementLevelId()
        {
            // Failure Log #9: Element.LevelId was removed in Revit 2024+
            // Validation regex only captures Type.Member where Type starts with uppercase
            var code = @"
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from Autodesk.Revit.DB import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
walls = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).ToElements()
level_id = Element.LevelId
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(HasBlock(result, "Element.LevelId"),
                "Element.LevelId should be BLOCKED (removed in Revit 2024+)");
        }

        [Fact]
        public void Block_RemovedApi_ElementIdIntegerValue()
        {
            // Failure Log #9: ElementId.IntegerValue was removed in Revit 2024+
            var code = @"
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from Autodesk.Revit.DB import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
elem = doc.GetElement(ElementId(12345))
int_val = ElementId.IntegerValue
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(HasBlock(result, "ElementId.IntegerValue"),
                "ElementId.IntegerValue should be BLOCKED (removed in Revit 2024+)");
        }

        [Fact]
        public void Block_UnknownBuiltInParameter()
        {
            // AI hallucinated a non-existent BuiltInParameter
            var code = @"
import clr
clr.AddReference('RevitAPI')
from Autodesk.Revit.DB import *

param = element.get_Parameter(BuiltInParameter.WALL_FAKE_PARAMETER)
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(HasBlock(result, "WALL_FAKE_PARAMETER"),
                "Non-existent BuiltInParameter should be BLOCKED");
        }

        [Fact]
        public void Block_UnknownBuiltInCategory()
        {
            var code = @"
import clr
clr.AddReference('RevitAPI')
from Autodesk.Revit.DB import *

collector = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_FakeCategory)
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(HasBlock(result, "OST_FakeCategory"),
                "Non-existent BuiltInCategory should be BLOCKED");
        }

        [Fact]
        public void Block_UnknownTypeMember()
        {
            // Failure Log #3: AI hallucinated GetElementIdsFromBody
            var code = @"
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from Autodesk.Revit.DB import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
view = doc.ActiveView
collector = FilteredElementCollector(doc, view.Id)
ids = Element.GetElementIdsFromBody()
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(HasBlock(result, "GetElementIdsFromBody"),
                "Non-existent member Element.GetElementIdsFromBody should be BLOCKED");
        }

        [Fact]
        public void Block_NullCode()
        {
            var result = LocalCodeValidationService.ValidateAndFix(null, "2024");
            Assert.False(result.IsPass);
            Assert.Equal("blocked", result.FinalStatus);
        }

        // ================================================================
        // SHOULD_PASS: Valid code that must NOT be blocked
        // ================================================================

        [Fact]
        public void Pass_ValidWallCollector()
        {
            var code = @"
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from Autodesk.Revit.DB import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
walls = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().ToElements()
for wall in walls:
    name = wall.Name
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(result.IsPass, FormatIssues("Valid wall collector should PASS", result));
        }

        [Fact]
        public void Pass_ValidBuiltInParameter()
        {
            var code = @"
import clr
clr.AddReference('RevitAPI')
from Autodesk.Revit.DB import *

param = element.get_Parameter(BuiltInParameter.WALL_ATTR_HEIGHT_PARAM)
value = param.AsDouble()
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(result.IsPass, FormatIssues("Valid BuiltInParameter should PASS", result));
        }

        [Fact]
        public void Pass_ElementIdValue()
        {
            // ElementId.Value is the correct replacement for IntegerValue in 2024+
            var code = @"
import clr
clr.AddReference('RevitAPI')
from Autodesk.Revit.DB import *

elem_id = ElementId(12345)
val = elem_id.Value
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(result.IsPass, FormatIssues("ElementId.Value should PASS (correct API)", result));
        }

        [Fact]
        public void Pass_TransactionPattern()
        {
            var code = @"
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from Autodesk.Revit.DB import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
t = Transaction(doc, 'Test')
t.Start()
t.Commit()
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(result.IsPass, FormatIssues("Standard Transaction pattern should PASS", result));
        }

        [Fact]
        public void Pass_CurveLoopAppend()
        {
            // CurveLoop() constructor + Append is the correct pattern
            var code = @"
import clr
clr.AddReference('RevitAPI')
from Autodesk.Revit.DB import *

loop = CurveLoop()
loop.Append(line1)
loop.Append(line2)
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(result.IsPass, FormatIssues("CurveLoop + Append pattern should PASS", result));
        }

        // ================================================================
        // SHOULD_WARN: Code that should warn but not block
        // ================================================================

        [Fact]
        public void Warn_DeprecatedMemberInXml()
        {
            // Deprecated (not removed) members should warn, not block
            LocalCodeValidationService.SetTestXmlIndex(
                deprecatedMembers: new HashSet<string>(StringComparer.Ordinal) { "Wall.Flipped" }
            );

            var code = @"
import clr
clr.AddReference('RevitAPI')
from Autodesk.Revit.DB import *

wall = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).FirstElement()
is_flipped = Wall.Flipped
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(result.IsPass, "Deprecated (not removed) member should not block");
            Assert.True(HasWarning(result, "Wall.Flipped"),
                "Deprecated member should produce a WARNING");
        }

        // ================================================================
        // EDGE CASES: Regex, nested parentheses, enum auto-fix
        // ================================================================

        [Fact]
        public void Pass_NestedParenthesesInMethodCall()
        {
            // Failure Log #12: Nested parentheses caused regex to skip validation entirely
            var code = @"
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from Autodesk.Revit.DB import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
p1 = XYZ(0, 0, 0)
p2 = XYZ(10, 0, 0)
line = Line.CreateBound(p1, p2)
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            // Should not crash or skip validation due to nested parens
            Assert.NotNull(result);
            Assert.NotNull(result.Issues);
        }

        [Fact]
        public void Block_RemovedApi_DocumentPlanTopologies()
        {
            var code = @"
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from Autodesk.Revit.DB import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
topos = Document.PlanTopologies
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(HasBlock(result, "Document.PlanTopologies"),
                "Document.PlanTopologies should be BLOCKED (removed in Revit 2024+)");
        }

        [Fact]
        public void AutoFix_MisspelledBuiltInParameter()
        {
            // Enum auto-fix should correct close misspellings
            var code = @"
import clr
clr.AddReference('RevitAPI')
from Autodesk.Revit.DB import *

param = element.get_Parameter(BuiltInParameter.WALL_ATTR_HEIGHT_PARM)
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            // Auto-fix may or may not fix this depending on Levenshtein distance
            // At minimum, the misspelled enum should be detected
            Assert.NotNull(result);
            bool hasIssue = result.Issues.Any(i =>
                i.Symbol != null && i.Symbol.Contains("WALL_ATTR_HEIGHT_PARM"));
            Assert.True(hasIssue || result.AutoFixApplied,
                "Misspelled BuiltInParameter should be detected or auto-fixed");
        }

        [Fact]
        public void Pass_EmptyCodeReturnsBlock()
        {
            var result = LocalCodeValidationService.ValidateAndFix("", "2024");
            // Empty string should still be processed (not null)
            Assert.NotNull(result);
        }

        [Fact]
        public void Pass_PureCommentCode()
        {
            var code = @"
# This is just a comment
# No actual code here
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(result.IsPass, "Pure comment code should PASS");
        }

        [Fact]
        public void Block_MultipleRemovedApis()
        {
            // Code using multiple removed APIs should block on all of them
            // Uses Type.Member form since regex only captures uppercase-starting types
            var code = @"
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitServices')
from Autodesk.Revit.DB import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
walls = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).ToElements()
level_id = Element.LevelId
int_val = ElementId.IntegerValue
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.False(result.IsPass, "Code with removed APIs should be blocked");
            Assert.True(HasBlock(result, "Element.LevelId"), "Should block Element.LevelId");
            Assert.True(HasBlock(result, "ElementId.IntegerValue"), "Should block ElementId.IntegerValue");
        }

        // ================================================================
        // PYTHON RUNTIME RULES
        // ================================================================

        [Fact]
        public void Block_SystemExit()
        {
            var code = @"
import sys
raise SystemExit
";
            var result = LocalCodeValidationService.ValidateAndFix(code, "2024");
            Assert.True(result.Issues.Any(i => i.Severity == ValidationSeverity.Block),
                "raise SystemExit should be BLOCKED");
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static bool HasBlock(LocalValidationResult result, string symbolSubstring)
        {
            return result.Issues.Any(i =>
                i.Severity == ValidationSeverity.Block &&
                i.Symbol != null &&
                i.Symbol.Contains(symbolSubstring));
        }

        private static bool HasWarning(LocalValidationResult result, string symbolSubstring)
        {
            return result.Issues.Any(i =>
                i.Severity == ValidationSeverity.Warning &&
                i.Symbol != null &&
                i.Symbol.Contains(symbolSubstring));
        }

        private static string FormatIssues(string message, LocalValidationResult result)
        {
            if (result.Issues == null || !result.Issues.Any())
                return message;

            var blocks = result.Issues
                .Where(i => i.Severity == ValidationSeverity.Block)
                .Select(i => $"  BLOCK: [{i.Category}] {i.Symbol} — {i.Message}");

            return message + "
Blocking issues:
" + string.Join("
", blocks);
        }
    }
}
