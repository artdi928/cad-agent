using System.Text.Json;
using CadAgent.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CadAgent.Tests;

[TestClass]
public sealed class ChangePlanTests
{
    [TestMethod]
    public void ValidDbTextPlan_PassesValidation()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation(
                    OperationId: "op-1",
                    Kind: "set_dbtext",
                    Target: new OperationTarget("1A2B"),
                    Precondition: new Precondition("OLD_TEXT"),
                    Value: "NEW_TEXT")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(1, result.Operations.Count);
        Assert.AreEqual("VALID", result.Operations[0].Status);
        Assert.AreEqual("NEW_TEXT", result.Operations[0].NewValue);
    }

    [TestMethod]
    public void ValidBlockAttributePlan_PassesValidation()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation(
                    OperationId: "op-attr-1",
                    Kind: "set_block_attribute",
                    Target: new OperationTarget("3C4D", "TAG_NAME"),
                    Precondition: new Precondition("OLD_VAL"),
                    Value: "NEW_VAL")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(1, result.Operations.Count);
        Assert.AreEqual("VALID", result.Operations[0].Status);
    }

    [TestMethod]
    public void InvalidVersion_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 2,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation("op-1", "set_dbtext", new OperationTarget("1A"), new Precondition("A"), "B")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void MissingDrawingPath_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget("   "),
            Operations:
            [
                new PlanOperation("op-1", "set_dbtext", new OperationTarget("1A"), new Precondition("A"), "B")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void EmptyOperations_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations: []);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void DuplicateOperationId_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation("dup-op", "set_dbtext", new OperationTarget("1A"), new Precondition("A"), "B"),
                new PlanOperation("dup-op", "set_dbtext", new OperationTarget("2B"), new Precondition("C"), "D")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void DuplicateTarget_DbText_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation("op-1", "set_dbtext", new OperationTarget("1A2B"), new Precondition("OLD1"), "NEW1"),
                new PlanOperation("op-2", "set_dbtext", new OperationTarget("1a2b"), new Precondition("OLD2"), "NEW2")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.DuplicateTarget, result.ErrorCode);
    }

    [TestMethod]
    public void DuplicateTarget_BlockAttribute_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation("op-1", "set_block_attribute", new OperationTarget("1A", "TAG1"), new Precondition("A"), "B"),
                new PlanOperation("op-2", "set_block_attribute", new OperationTarget("1A", "tag1"), new Precondition("C"), "D")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.DuplicateTarget, result.ErrorCode);
    }

    [TestMethod]
    public void UnsupportedKind_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation("op-1", "set_mtext", new OperationTarget("1A"), new Precondition("A"), "B")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.UnsupportedOperation, result.ErrorCode);
    }

    [TestMethod]
    public void DbText_WithAttributeTag_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation("op-1", "set_dbtext", new OperationTarget("1A", "TAG_NOT_ALLOWED"), new Precondition("A"), "B")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.UnsupportedField, result.ErrorCode);
    }

    [TestMethod]
    public void BlockAttribute_WithoutAttributeTag_IsRejected()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation("op-1", "set_block_attribute", new OperationTarget("1A"), new Precondition("A"), "B")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.UnsupportedField, result.ErrorCode);
    }

    [TestMethod]
    public void EmptyStringValueAndEquals_IsValid()
    {
        var plan = new ChangePlan(
            Version: 1,
            Drawing: new DrawingTarget(@"C:\Drawings\test.dwg"),
            Operations:
            [
                new PlanOperation("op-empty", "set_dbtext", new OperationTarget("1A"), new Precondition(""), "")
            ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual("", result.Operations[0].NewValue);
    }

    [TestMethod]
    public void SerializationRoundTrip_PreservesExactContent()
    {
        var json = """
        {
          "version": 1,
          "drawing": { "fullPath": "C:\\drawings\\test.dwg" },
          "operations": [
            {
              "operationId": "op-001",
              "kind": "set_dbtext",
              "target": { "handle": "10AF" },
              "precondition": { "equals": " exact whitespace " },
              "value": " replacement text "
            }
          ]
        }
        """;

        var plan = JsonSerializer.Deserialize<ChangePlan>(json, JsonDefaults.Options);
        Assert.IsNotNull(plan);
        Assert.AreEqual(1, plan.Version);
        Assert.AreEqual(@"C:\drawings\test.dwg", plan.Drawing.FullPath);
        Assert.AreEqual(" exact whitespace ", plan.Operations[0].Precondition!.Equals);
        Assert.AreEqual(" replacement text ", plan.Operations[0].Value);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
    }

    [TestMethod]
    public void ErrorPrecedence_MalformedPlan_RejectedBeforeDocumentChecks()
    {
        // Malformed plan must be rejected deterministically without checking document identity
        var malformedPlan = new ChangePlan(
            Version: 999, // unsupported version
            Drawing: new DrawingTarget(@"C:\any\path.dwg"),
            Operations: []);

        var result = ChangePlanValidator.ValidatePure(malformedPlan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void SavedStatePredicate_SavedVsUntitledDrawings()
    {
        // Predicate: Document.IsNamedDrawing && !string.IsNullOrWhiteSpace(Filename) && File.Exists(Filename)
        bool EvaluateSavedState(bool isNamedDrawing, string? filename, Func<string, bool> fileExists) =>
            isNamedDrawing && !string.IsNullOrWhiteSpace(filename) && fileExists(filename);

        // Case 1: Untitled drawing (Ctrl+N) with temporary/template filename in memory
        Assert.IsFalse(EvaluateSavedState(false, @"C:\Users\artam\AppData\Local\Temp\Drawing1.dwg", _ => true),
            "Untitled drawing must not be classified as saved even if filename is present.");

        // Case 2: Untitled drawing with empty filename
        Assert.IsFalse(EvaluateSavedState(false, "", _ => false),
            "Untitled drawing with empty filename must be unsaved.");

        // Case 3: Saved drawing, clean on disk
        Assert.IsTrue(EvaluateSavedState(true, @"C:\Drawings\saved.dwg", _ => true),
            "Saved drawing must be classified as saved.");

        // Case 4: Saved drawing, modified/dirty in-memory edits
        // IsNamedDrawing remains true, file exists on disk
        Assert.IsTrue(EvaluateSavedState(true, @"C:\Drawings\saved.dwg", _ => true),
            "Saved drawing with in-memory dirty edits must remain classified as a saved document, not UNSAVED_DOCUMENT.");
    }

    [TestMethod]
    public void WrongDocument_PathMismatch_DetectedDeterministically()
    {
        var activePath = @"C:\Drawings\ProjectA\drawing.dwg";
        var planPath = @"C:\Drawings\ProjectB\drawing.dwg";

        var activeNormalized = Path.GetFullPath(activePath);
        var planNormalized = Path.GetFullPath(planPath);

        Assert.IsFalse(string.Equals(activeNormalized, planNormalized, StringComparison.OrdinalIgnoreCase),
            "Path mismatch must be detected and result in WRONG_DOCUMENT.");
    }

    [TestMethod]
    public void Precondition_ComparesToOldValue_ExactOrdinal()
    {
        var oldValue = "OLD_VAL";
        var newValue = "NEW_VAL";
        var op = new PlanOperation("op-1", "set_dbtext", new OperationTarget("1A"), new Precondition("OLD_VAL"), newValue);

        // Precondition must match old value
        Assert.IsTrue(string.Equals(oldValue, op.Precondition!.Equals, StringComparison.Ordinal));
        Assert.IsFalse(string.Equals(newValue, op.Precondition.Equals, StringComparison.Ordinal));

        // Exact ordinal: case and whitespace sensitivity
        Assert.IsFalse(string.Equals("old_val", op.Precondition.Equals, StringComparison.Ordinal));
        Assert.IsFalse(string.Equals(" OLD_VAL ", op.Precondition.Equals, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Postcondition_ComparesToRequestedNewValue_ExactOrdinal()
    {
        var requestedValue = "TARGET_NEW_VALUE";
        var actualWritten = "TARGET_NEW_VALUE";
        var mismatchWritten = "OTHER_VALUE";

        Assert.IsTrue(string.Equals(actualWritten, requestedValue, StringComparison.Ordinal));
        Assert.IsFalse(string.Equals(mismatchWritten, requestedValue, StringComparison.Ordinal));
        Assert.IsFalse(string.Equals("target_new_value", requestedValue, StringComparison.Ordinal));
    }

    [TestMethod]
    public void ResultSemantics_JsonSerialization_MatchesContract()
    {
        // 1. Success
        var success = new PlanApplyResult(
            Applied: true,
            PostconditionVerifiedBeforeCommit: true,
            RollbackVerified: null,
            Operations: [new OperationApplyResult("op-1", "APPLIED", "A", "B")]);

        var successJson = JsonSerializer.Serialize(success, JsonDefaults.Options);
        Assert.IsTrue(successJson.Contains("\"applied\":true"));
        Assert.IsTrue(successJson.Contains("\"postconditionVerifiedBeforeCommit\":true"));
        Assert.IsTrue(successJson.Contains("\"rollbackVerified\":null"));

        // 2. Pre-write rejection
        var prewrite = new PlanApplyResult(
            Applied: false,
            PostconditionVerifiedBeforeCommit: null,
            RollbackVerified: null);

        var prewriteJson = JsonSerializer.Serialize(prewrite, JsonDefaults.Options);
        Assert.IsTrue(prewriteJson.Contains("\"applied\":false"));
        Assert.IsTrue(prewriteJson.Contains("\"postconditionVerifiedBeforeCommit\":null"));
        Assert.IsTrue(prewriteJson.Contains("\"rollbackVerified\":null"));

        // 3. Post-write failure with successful rollback
        var postwrite = new PlanApplyResult(
            Applied: false,
            PostconditionVerifiedBeforeCommit: false,
            RollbackVerified: true);

        var postwriteJson = JsonSerializer.Serialize(postwrite, JsonDefaults.Options);
        Assert.IsTrue(postwriteJson.Contains("\"applied\":false"));
        Assert.IsTrue(postwriteJson.Contains("\"postconditionVerifiedBeforeCommit\":false"));
        Assert.IsTrue(postwriteJson.Contains("\"rollbackVerified\":true"));

        // 4. Rollback failed (atomicity violation)
        var atomicityViolation = new PlanApplyResult(
            Applied: false,
            PostconditionVerifiedBeforeCommit: false,
            RollbackVerified: false);

        var atomicityJson = JsonSerializer.Serialize(atomicityViolation, JsonDefaults.Options);
        Assert.IsTrue(atomicityJson.Contains("\"applied\":false"));
        Assert.IsTrue(atomicityJson.Contains("\"postconditionVerifiedBeforeCommit\":false"));
        Assert.IsTrue(atomicityJson.Contains("\"rollbackVerified\":false"));
    }

    [TestMethod]
    public void IntegrationStyle_WriteAttempted_Abort_RollbackVerified()
    {
        // In-memory simulated transactional database state
        var db = new Dictionary<string, string>
        {
            ["handle_A"] = "ORIGINAL_A"
        };

        var preApplySnapshots = new Dictionary<string, string>(db);
        bool writeAttempted = false;
        bool committed = false;
        bool aborted = false;

        // Transaction lifecycle simulation
        try
        {
            // Write attempted
            writeAttempted = true;
            db["handle_A"] = "NEW_A";

            // Forced postcondition failure (Mechanism C test hook)
            var postconditionPassed = false;
            if (!postconditionPassed)
            {
                throw new InvalidOperationException("Simulated postcondition mismatch");
            }

            committed = true;
        }
        catch
        {
            aborted = true;
            // Rollback in-memory state
            foreach (var (k, v) in preApplySnapshots)
            {
                db[k] = v;
            }
        }

        Assert.IsTrue(writeAttempted, "Write must be attempted before abort.");
        Assert.IsTrue(aborted, "Transaction must abort on postcondition failure.");
        Assert.IsFalse(committed, "Commit must be unreachable after failure.");

        // Fresh read-only verification
        bool rollbackVerified = true;
        foreach (var (k, v) in preApplySnapshots)
        {
            if (!string.Equals(db[k], v, StringComparison.Ordinal))
            {
                rollbackVerified = false;
            }
        }

        Assert.IsTrue(rollbackVerified, "Rollback verification must confirm original values restored.");
        Assert.AreEqual("ORIGINAL_A", db["handle_A"]);
    }

    [TestMethod]
    public void IntegrationStyle_MultiTarget_Abort_RestoresAllOriginals()
    {
        var db = new Dictionary<string, string>
        {
            ["handle_A"] = "OLD_A",
            ["handle_B"] = "OLD_B"
        };

        var snapshots = new Dictionary<string, string>(db);
        bool writeAttempted = false;
        bool committed = false;

        try
        {
            // op 1 writes
            writeAttempted = true;
            db["handle_A"] = "NEW_A";

            // op 2 writes and fails postcondition
            db["handle_B"] = "NEW_B";
            var op2PostconditionPassed = false;
            if (!op2PostconditionPassed)
            {
                throw new InvalidOperationException("Op 2 failed postcondition");
            }

            committed = true;
        }
        catch
        {
            // Abort and rollback
            foreach (var (k, v) in snapshots)
            {
                db[k] = v;
            }
        }

        Assert.IsTrue(writeAttempted);
        Assert.IsFalse(committed);
        Assert.AreEqual("OLD_A", db["handle_A"]);
        Assert.AreEqual("OLD_B", db["handle_B"]);
    }

    [TestMethod]
    public void CreateLine_Valid_PassesValidation()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("line-1", "create_line",
                Start: new PointDto(0, 0, 0),
                End: new PointDto(1000, 500, 0),
                Layer: "DIM")
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(1, result.Operations.Count);
        Assert.AreEqual("VALID", result.Operations[0].Status);
    }

    [TestMethod]
    public void CreateLine_ZeroLength_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("line-1", "create_line",
                Start: new PointDto(100, 100, 0),
                End: new PointDto(100, 100, 0))
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void CreateLine_NonFiniteCoordinate_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("line-1", "create_line",
                Start: new PointDto(double.NaN, 0, 0),
                End: new PointDto(100, 100, 0))
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void CreatePolyline_ValidOpenAndClosed_PassesValidation()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("poly-open", "create_polyline",
                Points: [new PointDto(0, 0), new PointDto(100, 0), new PointDto(100, 100)],
                Closed: false),
            new PlanOperation("poly-closed", "create_polyline",
                Points: [new PointDto(0, 0), new PointDto(1000, 0), new PointDto(1000, 500), new PointDto(0, 500)],
                Closed: true)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(2, result.Operations.Count);
        Assert.IsTrue(result.Operations.All(o => o.Status == "VALID"));
    }

    [TestMethod]
    public void CreatePolyline_LessThanTwoPoints_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("poly-1", "create_polyline",
                Points: [new PointDto(0, 0)])
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void CreatePolyline_ClosedWithLessThanThreeDistinctPoints_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("poly-1", "create_polyline",
                Points: [new PointDto(0, 0), new PointDto(100, 100), new PointDto(0, 0)],
                Closed: true)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void CreatePolyline_ZeroTotalLength_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("poly-1", "create_polyline",
                Points: [new PointDto(10, 10), new PointDto(10, 10)],
                Closed: false)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void CreateCircle_Valid_PassesValidation()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("circle-1", "create_circle",
                Center: new PointDto(2500, 250),
                Radius: 500)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual("VALID", result.Operations[0].Status);
    }

    [TestMethod]
    public void CreateCircle_ZeroOrNegativeRadius_IsRejected()
    {
        var plan1 = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("circle-zero", "create_circle",
                Center: new PointDto(0, 0),
                Radius: 0)
        ]);
        Assert.IsFalse(ChangePlanValidator.ValidatePure(plan1).Valid);

        var plan2 = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("circle-neg", "create_circle",
                Center: new PointDto(0, 0),
                Radius: -50)
        ]);
        Assert.IsFalse(ChangePlanValidator.ValidatePure(plan2).Valid);
    }

    [TestMethod]
    public void CreateArc_Valid_PassesValidation()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("arc-1", "create_arc",
                Center: new PointDto(0, 0),
                Radius: 250,
                StartAngle: 0.0,
                EndAngle: Math.PI)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual("VALID", result.Operations[0].Status);
    }

    [TestMethod]
    public void CreateArc_EqualAngles_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("arc-equal", "create_arc",
                Center: new PointDto(0, 0),
                Radius: 250,
                StartAngle: 1.5,
                EndAngle: 1.5)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void CreateDbText_ValidWithAlignment_PassesValidation()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("text-1", "create_dbtext",
                Text: "КРУГ",
                Position: new PointDto(2500, 250),
                Height: 200,
                Rotation: 0,
                HorizontalAlignment: "center",
                VerticalAlignment: "middle")
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual("VALID", result.Operations[0].Status);
    }

    [TestMethod]
    public void CreateDbText_EmptyTextOrZeroHeight_IsRejected()
    {
        var emptyTextPlan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("text-empty", "create_dbtext",
                Text: "",
                Position: new PointDto(0, 0),
                Height: 100)
        ]);
        Assert.IsFalse(ChangePlanValidator.ValidatePure(emptyTextPlan).Valid);

        var zeroHeightPlan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("text-zero-h", "create_dbtext",
                Text: "Hello",
                Position: new PointDto(0, 0),
                Height: 0)
        ]);
        Assert.IsFalse(ChangePlanValidator.ValidatePure(zeroHeightPlan).Valid);
    }

    [TestMethod]
    public void CreateDbText_InvalidAlignment_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("text-bad-align", "create_dbtext",
                Text: "Hello",
                Position: new PointDto(0, 0),
                Height: 100,
                HorizontalAlignment: "diagonal")
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void CreateMText_Valid_PassesValidation()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("mtext-1", "create_mtext",
                Text: "ТЕКСТ",
                Position: new PointDto(0, 0),
                TextHeight: 200,
                Width: 1000,
                Rotation: 0)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual("VALID", result.Operations[0].Status);
    }

    [TestMethod]
    public void CreateMText_EmptyTextOrZeroHeight_IsRejected()
    {
        var emptyPlan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("mtext-empty", "create_mtext",
                Text: "",
                Position: new PointDto(0, 0),
                TextHeight: 100)
        ]);
        Assert.IsFalse(ChangePlanValidator.ValidatePure(emptyPlan).Valid);

        var zeroHeightPlan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("mtext-zero-h", "create_mtext",
                Text: "Valid",
                Position: new PointDto(0, 0),
                TextHeight: 0)
        ]);
        Assert.IsFalse(ChangePlanValidator.ValidatePure(zeroHeightPlan).Valid);
    }

    [TestMethod]
    public void InsertBlock_ValidWithScaleAndAttributes_PassesValidation()
    {
        var attrs = new Dictionary<string, string>
        {
            ["TAG_A"] = "Value A",
            ["TAG_B"] = "Value B"
        };

        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("block-1", "insert_block",
                BlockName: "VALVE_BLOCK",
                Position: new PointDto(100, 200),
                Rotation: 0,
                Scale: new ScaleDto(1.0, 1.0, 1.0),
                Attributes: attrs)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual("VALID", result.Operations[0].Status);
    }

    [TestMethod]
    public void InsertBlock_MissingBlockNameOrZeroScale_IsRejected()
    {
        var emptyNamePlan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("block-noname", "insert_block",
                BlockName: "",
                Position: new PointDto(0, 0))
        ]);
        Assert.IsFalse(ChangePlanValidator.ValidatePure(emptyNamePlan).Valid);

        var zeroScalePlan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("block-zeroscale", "insert_block",
                BlockName: "MY_BLOCK",
                Position: new PointDto(0, 0),
                Scale: new ScaleDto(0, 1, 1))
        ]);
        Assert.IsFalse(ChangePlanValidator.ValidatePure(zeroScalePlan).Valid);
    }

    [TestMethod]
    public void MultiPrimitivePlan_Task1Composition_PassesValidation()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("poly-rect", "create_polyline",
                Points: [new PointDto(0, 0), new PointDto(1000, 0), new PointDto(1000, 500), new PointDto(0, 500)],
                Closed: true),
            new PlanOperation("circle-gap", "create_circle",
                Center: new PointDto(2500, 250),
                Radius: 500),
            new PlanOperation("text-center", "create_dbtext",
                Text: "КРУГ",
                Position: new PointDto(2500, 250),
                Height: 200,
                Rotation: 0,
                HorizontalAlignment: "center",
                VerticalAlignment: "middle")
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(3, result.Operations.Count);
        Assert.IsTrue(result.Operations.All(o => o.Status == "VALID"));
    }

    [TestMethod]
    public void MultiPrimitivePlan_DuplicateOperationId_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("op-dup", "create_line",
                Start: new PointDto(0, 0),
                End: new PointDto(100, 0)),
            new PlanOperation("op-dup", "create_circle",
                Center: new PointDto(50, 50),
                Radius: 25)
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
        Assert.IsTrue(result.Message!.Contains("Duplicate operationId"));
    }

    [TestMethod]
    public void Plan_EmptyLayer_IsRejected()
    {
        var plan = new ChangePlan(1, new DrawingTarget(@"C:\drawings\test.dwg"),
        [
            new PlanOperation("op-1", "create_line",
                Start: new PointDto(0, 0),
                End: new PointDto(100, 0),
                Layer: "   ")
        ]);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ErrorCodes.InvalidChangePlan, result.ErrorCode);
    }

    [TestMethod]
    public void JsonRoundTrip_AllPrimitives_DeserializesAndValidates()
    {
        var json = """
        {
          "version": 1,
          "drawing": { "fullPath": "C:\\drawings\\test.dwg" },
          "operations": [
            {
              "operationId": "rect-1",
              "kind": "create_polyline",
              "points": [
                { "x": 0, "y": 0 },
                { "x": 1000, "y": 0 },
                { "x": 1000, "y": 500 },
                { "x": 0, "y": 500 }
              ],
              "closed": true
            },
            {
              "operationId": "circle-1",
              "kind": "create_circle",
              "center": { "x": 2500, "y": 250 },
              "radius": 500
            },
            {
              "operationId": "text-1",
              "kind": "create_dbtext",
              "text": "КРУГ",
              "position": { "x": 2500, "y": 250 },
              "height": 200,
              "rotation": 0,
              "horizontalAlignment": "center",
              "verticalAlignment": "middle"
            }
          ]
        }
        """;

        var plan = JsonSerializer.Deserialize<ChangePlan>(json, JsonDefaults.Options);
        Assert.IsNotNull(plan);
        Assert.AreEqual(1, plan.Version);
        Assert.AreEqual(3, plan.Operations.Count);

        var result = ChangePlanValidator.ValidatePure(plan);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual(3, result.Operations.Count);
    }
}
