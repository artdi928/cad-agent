using System.Globalization;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CadAgent.Protocol;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadAgent.Plugin;

internal static class PlanExecutor
{
    public static RpcResponse ValidatePlan(RpcRequest request)
    {
        var plan = ParsePlan(request);
        if (plan is null)
            return RpcResponse.Failure(request.RequestId, ErrorCodes.InvalidChangePlan, "Missing or malformed 'plan' parameter.");

        var pureValidation = ChangePlanValidator.ValidatePure(plan);
        if (!pureValidation.Valid)
        {
            return RpcResponse.Success(request.RequestId, pureValidation);
        }

        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
            return RpcResponse.Failure(request.RequestId, ErrorCodes.CadBusy, "AutoCAD has no active document.");

        var (docValid, docError, docMsg) = ValidateDocumentIdentity(document, plan.Drawing.FullPath);
        if (!docValid)
        {
            var opResults = plan.Operations.Select(op =>
                new OperationValidationResult(op.OperationId, docError!, Message: docMsg)).ToList();
            return RpcResponse.Success(request.RequestId, new PlanValidationResult(false, opResults, docError, docMsg));
        }

        var dryRunResult = PerformDryRunRead(document, plan);
        return RpcResponse.Success(request.RequestId, dryRunResult);
    }

    public static RpcResponse ApplyPlan(RpcRequest request)
    {
        var plan = ParsePlan(request);
        if (plan is null)
            return PreWriteRejection(request.RequestId, ErrorCodes.InvalidChangePlan, "Missing or malformed 'plan' parameter.");

        // 1. Pure validation
        var pureValidation = ChangePlanValidator.ValidatePure(plan);
        if (!pureValidation.Valid)
        {
            return PreWriteRejection(request.RequestId, pureValidation.ErrorCode ?? ErrorCodes.InvalidChangePlan, pureValidation.Message ?? "Invalid change plan.");
        }

        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
            return PreWriteRejection(request.RequestId, ErrorCodes.CadBusy, "AutoCAD has no active document.");

        // 2. Validate active saved drawing identity
        var (docValid, docError, docMsg) = ValidateDocumentIdentity(document, plan.Drawing.FullPath);
        if (!docValid)
        {
            return PreWriteRejection(request.RequestId, docError!, docMsg ?? "Document identity validation failed.");
        }

        // 3-8. Preflight validation ForRead (all targets, entity types, fields/tags, preconditions)
        var preflight = PerformDryRunRead(document, plan);
        if (!preflight.Valid)
        {
            var firstFail = preflight.Operations.FirstOrDefault(o => o.Status != "VALID");
            var errorCode = firstFail?.Status ?? preflight.ErrorCode ?? ErrorCodes.PreconditionFailed;
            var message = firstFail?.Message ?? preflight.Message ?? "Preflight validation failed.";
            return PreWriteRejection(request.RequestId, errorCode, message);
        }

        // Preflight 100% passed! 0 writes have occurred up to this point.
        // 9. Acquire DocumentLock
        using (document.LockDocument())
        {
            // Capture pre-apply original values for authoritative rollback verification
            var preApplySnapshots = new List<TargetSnapshot>();
            using (var snapTx = document.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var op in plan.Operations)
                {
                    TryParseHandle(op.Target.Handle, out var h);
                    document.Database.TryGetObjectId(h, out var objId);
                    if (string.Equals(op.Kind, ChangePlanValidator.KindSetDbText, StringComparison.Ordinal))
                    {
                        var dt = (DBText)snapTx.GetObject(objId, OpenMode.ForRead);
                        preApplySnapshots.Add(new TargetSnapshot(op.OperationId, op.Kind, op.Target.Handle, null, dt.TextString));
                    }
                    else if (string.Equals(op.Kind, ChangePlanValidator.KindSetBlockAttribute, StringComparison.Ordinal))
                    {
                        var blk = (BlockReference)snapTx.GetObject(objId, OpenMode.ForRead);
                        string origVal = "";
                        foreach (ObjectId attrId in blk.AttributeCollection)
                        {
                            if (snapTx.GetObject(attrId, OpenMode.ForRead) is AttributeReference ar &&
                                string.Equals(ar.Tag, op.Target.AttributeTag, StringComparison.OrdinalIgnoreCase))
                            {
                                origVal = ar.TextString;
                                break;
                            }
                        }
                        preApplySnapshots.Add(new TargetSnapshot(op.OperationId, op.Kind, op.Target.Handle, op.Target.AttributeTag, origVal));
                    }
                }
            }

            bool writeAttempted = false;
            var touchedSnapshots = new List<TargetSnapshot>();
            var appliedOperations = new List<OperationApplyResult>();

            // 10. Start single mutation transaction
            using var transaction = document.TransactionManager.StartTransaction();
            try
            {
                // 11-12. Open exact validated targets ForWrite and apply allowlisted changes
                for (int i = 0; i < plan.Operations.Count; i++)
                {
                    var op = plan.Operations[i];
                    var snapshot = preApplySnapshots[i];

                    if (!TryParseHandle(op.Target.Handle, out var handle) ||
                        !document.Database.TryGetObjectId(handle, out var objectId))
                    {
                        throw new PlanExecutionException(ErrorCodes.TargetNotFound,
                            $"Target handle '{op.Target.Handle}' not found during write phase.");
                    }

                    if (string.Equals(op.Kind, ChangePlanValidator.KindSetDbText, StringComparison.Ordinal))
                    {
                        var dbText = (DBText)transaction.GetObject(objectId, OpenMode.ForWrite);
                        var previousValue = dbText.TextString;

                        writeAttempted = true;
                        touchedSnapshots.Add(snapshot);

                        dbText.TextString = op.Value;

                        // 13. Re-read actual value INSIDE THE SAME TRANSACTION
                        var actualValue = dbText.TextString;

                        // 14. Validate postcondition: actual == requested value (exact ordinal string)
#if DEBUG
                        var forcedFault = string.Equals(op.FaultInjection, "force_postcondition_mismatch", StringComparison.Ordinal);
#else
                        const bool forcedFault = false;
#endif
                        if (forcedFault || !string.Equals(actualValue, op.Value, StringComparison.Ordinal))
                        {
                            throw new PlanExecutionException(ErrorCodes.PostconditionFailed,
                                $"Postcondition mismatch for DBText '{op.Target.Handle}': expected '{op.Value}', got '{actualValue}'.");
                        }

                        appliedOperations.Add(new OperationApplyResult(op.OperationId, "APPLIED", previousValue, op.Value));
                    }
                    else if (string.Equals(op.Kind, ChangePlanValidator.KindSetBlockAttribute, StringComparison.Ordinal))
                    {
                        var block = (BlockReference)transaction.GetObject(objectId, OpenMode.ForRead);
                        AttributeReference? targetAttr = null;

                        foreach (ObjectId attrId in block.AttributeCollection)
                        {
                            if (transaction.GetObject(attrId, OpenMode.ForRead) is AttributeReference attr &&
                                string.Equals(attr.Tag, op.Target.AttributeTag, StringComparison.OrdinalIgnoreCase))
                            {
                                targetAttr = (AttributeReference)transaction.GetObject(attrId, OpenMode.ForWrite);
                                break;
                            }
                        }

                        if (targetAttr is null)
                        {
                            throw new PlanExecutionException(ErrorCodes.PostconditionFailed,
                                $"Target attribute '{op.Target.AttributeTag}' missing during write phase.");
                        }

                        var previousValue = targetAttr.TextString;

                        writeAttempted = true;
                        touchedSnapshots.Add(snapshot);

                        targetAttr.TextString = op.Value;

                        // 13. Re-read actual value INSIDE THE SAME TRANSACTION
                        var actualValue = targetAttr.TextString;

                        // 14. Validate postcondition: actual == requested value (exact ordinal string)
#if DEBUG
                        var forcedFault = string.Equals(op.FaultInjection, "force_postcondition_mismatch", StringComparison.Ordinal);
#else
                        const bool forcedFault = false;
#endif
                        if (forcedFault || !string.Equals(actualValue, op.Value, StringComparison.Ordinal))
                        {
                            throw new PlanExecutionException(ErrorCodes.PostconditionFailed,
                                $"Postcondition mismatch for attribute '{op.Target.AttributeTag}' on block '{op.Target.Handle}': expected '{op.Value}', got '{actualValue}'.");
                        }

                        appliedOperations.Add(new OperationApplyResult(op.OperationId, "APPLIED", previousValue, op.Value));
                    }
                }

                // 15. All postconditions pass -> Commit!
                transaction.Commit();

                // 16. Return audit DTO with postconditionVerifiedBeforeCommit=true
                var applyResult = new PlanApplyResult(
                    Applied: true,
                    PostconditionVerifiedBeforeCommit: true,
                    RollbackVerified: null,
                    Operations: appliedOperations);

                return RpcResponse.Success(request.RequestId, applyResult);
            }
            catch (Exception ex)
            {
                try { transaction.Abort(); } catch { }

                var errorCode = (ex as PlanExecutionException)?.ErrorCode ?? ErrorCodes.CadError;
                var errorMessage = ex.Message;

                if (!writeAttempted)
                {
                    return PreWriteRejection(request.RequestId, errorCode, errorMessage);
                }

                // Fresh read-only transaction to verify rollback
                var rollbackSuccess = VerifyRollback(document, touchedSnapshots);

                string finalErrorCode;
                string finalMessage;
                if (rollbackSuccess)
                {
                    finalErrorCode = errorCode;
                    finalMessage = $"{errorMessage} Transaction aborted and rollback verified successfully.";
                }
                else
                {
                    finalErrorCode = ErrorCodes.AtomicityViolation;
                    finalMessage = $"{errorMessage} CRITICAL: Rollback verification failed! Database state did not restore to pre-apply state.";
                }

                var postwriteResult = new PlanApplyResult(
                    Applied: false,
                    PostconditionVerifiedBeforeCommit: false,
                    RollbackVerified: rollbackSuccess);

                return RpcResponse.Failure(request.RequestId, finalErrorCode, finalMessage, postwriteResult);
            }
        }
    }

    private static RpcResponse PreWriteRejection(string requestId, string errorCode, string message)
    {
        var result = new PlanApplyResult(
            Applied: false,
            PostconditionVerifiedBeforeCommit: null,
            RollbackVerified: null);
        return RpcResponse.Failure(requestId, errorCode, message, result);
    }

    private static bool VerifyRollback(Document document, IReadOnlyList<TargetSnapshot> snapshots)
    {
        try
        {
            using var verifyTx = document.TransactionManager.StartOpenCloseTransaction();
            foreach (var snap in snapshots)
            {
                if (!TryParseHandle(snap.Handle, out var handle) ||
                    !document.Database.TryGetObjectId(handle, out var objectId))
                {
                    return false;
                }

                if (string.Equals(snap.Kind, ChangePlanValidator.KindSetDbText, StringComparison.Ordinal))
                {
                    if (verifyTx.GetObject(objectId, OpenMode.ForRead) is not DBText dbText)
                        return false;
                    if (!string.Equals(dbText.TextString, snap.OriginalValue, StringComparison.Ordinal))
                        return false;
                }
                else if (string.Equals(snap.Kind, ChangePlanValidator.KindSetBlockAttribute, StringComparison.Ordinal))
                {
                    if (verifyTx.GetObject(objectId, OpenMode.ForRead) is not BlockReference block)
                        return false;
                    string? currentAttrVal = null;
                    foreach (ObjectId attrId in block.AttributeCollection)
                    {
                        if (verifyTx.GetObject(attrId, OpenMode.ForRead) is AttributeReference attr &&
                            string.Equals(attr.Tag, snap.AttributeTag, StringComparison.OrdinalIgnoreCase))
                        {
                            currentAttrVal = attr.TextString;
                            break;
                        }
                    }
                    if (!string.Equals(currentAttrVal, snap.OriginalValue, StringComparison.Ordinal))
                        return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (bool Valid, string? ErrorCode, string? Message) ValidateDocumentIdentity(Document document, string planDrawingPath)
    {
        // Saved document != clean document; saved document != dirty document.
        // Document.IsNamedDrawing is the authoritative .NET predicate for titled/saved drawing state (equivalent to DWGTITLED sysvar).
        if (!document.IsNamedDrawing || string.IsNullOrWhiteSpace(document.Database.Filename) || !File.Exists(document.Database.Filename))
        {
            return (false, ErrorCodes.UnsavedDocument, "Active drawing is not a saved file on disk. Mutations require an authoritative saved drawing.");
        }

        try
        {
            var activeNormalized = Path.GetFullPath(document.Database.Filename);
            var planNormalized = Path.GetFullPath(planDrawingPath);

            if (!string.Equals(activeNormalized, planNormalized, StringComparison.OrdinalIgnoreCase))
            {
                return (false, ErrorCodes.WrongDocument,
                    $"Active drawing '{activeNormalized}' does not match plan target drawing '{planNormalized}'.");
            }
        }
        catch (Exception ex)
        {
            return (false, ErrorCodes.InvalidChangePlan, $"Invalid drawing path: {ex.Message}");
        }

        return (true, null, null);
    }

    private sealed record TargetSnapshot(string OperationId, string Kind, string Handle, string? AttributeTag, string OriginalValue);

    private sealed class PlanExecutionException : Exception
    {
        public string ErrorCode { get; }
        public PlanExecutionException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }
    }

    private static PlanValidationResult PerformDryRunRead(Document document, ChangePlan plan)
    {
        using var transaction = document.TransactionManager.StartOpenCloseTransaction();
        var operationResults = new List<OperationValidationResult>();
        string? topErrorCode = null;
        string? topErrorMessage = null;

        foreach (var op in plan.Operations)
        {
            if (!TryParseHandle(op.Target.Handle, out var handle) ||
                !document.Database.TryGetObjectId(handle, out var objectId) ||
                objectId.IsNull || objectId.IsErased)
            {
                topErrorCode ??= ErrorCodes.TargetNotFound;
                topErrorMessage ??= $"Target handle '{op.Target.Handle}' not found in active drawing.";
                operationResults.Add(new OperationValidationResult(
                    op.OperationId, ErrorCodes.TargetNotFound, Message: $"Target handle '{op.Target.Handle}' not found."));
                continue;
            }

            var entity = transaction.GetObject(objectId, OpenMode.ForRead) as Entity;
            if (entity is null)
            {
                topErrorCode ??= ErrorCodes.TargetNotFound;
                topErrorMessage ??= $"Handle '{op.Target.Handle}' is not a database entity.";
                operationResults.Add(new OperationValidationResult(
                    op.OperationId, ErrorCodes.TargetNotFound, Message: "Object is not an Entity."));
                continue;
            }

            if (string.Equals(op.Kind, ChangePlanValidator.KindSetDbText, StringComparison.Ordinal))
            {
                if (entity is not DBText dbText)
                {
                    topErrorCode ??= ErrorCodes.EntityTypeMismatch;
                    topErrorMessage ??= $"Target '{op.Target.Handle}' is {entity.GetType().Name}, expected DBText.";
                    operationResults.Add(new OperationValidationResult(
                        op.OperationId, ErrorCodes.EntityTypeMismatch, Message: $"Expected DBText, found {entity.GetType().Name}."));
                    continue;
                }

                var current = dbText.TextString;
                // Exact ordinal comparison, no trimming or whitespace folding
                if (!string.Equals(current, op.Precondition.Equals, StringComparison.Ordinal))
                {
                    topErrorCode ??= ErrorCodes.PreconditionFailed;
                    topErrorMessage ??= $"Precondition failed for DBText '{op.Target.Handle}': expected '{op.Precondition.Equals}', actual is '{current}'.";
                    operationResults.Add(new OperationValidationResult(
                        op.OperationId, ErrorCodes.PreconditionFailed, CurrentValue: current, NewValue: op.Value,
                        Message: $"Precondition mismatch: expected '{op.Precondition.Equals}', found '{current}'."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(
                    op.OperationId, "VALID", CurrentValue: current, NewValue: op.Value));
            }
            else if (string.Equals(op.Kind, ChangePlanValidator.KindSetBlockAttribute, StringComparison.Ordinal))
            {
                if (entity is not BlockReference block)
                {
                    topErrorCode ??= ErrorCodes.EntityTypeMismatch;
                    topErrorMessage ??= $"Target '{op.Target.Handle}' is {entity.GetType().Name}, expected BlockReference.";
                    operationResults.Add(new OperationValidationResult(
                        op.OperationId, ErrorCodes.EntityTypeMismatch, Message: $"Expected BlockReference, found {entity.GetType().Name}."));
                    continue;
                }

                AttributeReference? targetAttr = null;
                foreach (ObjectId attrId in block.AttributeCollection)
                {
                    if (transaction.GetObject(attrId, OpenMode.ForRead) is AttributeReference attr &&
                        string.Equals(attr.Tag, op.Target.AttributeTag, StringComparison.OrdinalIgnoreCase))
                    {
                        targetAttr = attr;
                        break;
                    }
                }

                if (targetAttr is null)
                {
                    topErrorCode ??= ErrorCodes.PreconditionFailed;
                    topErrorMessage ??= $"Block '{op.Target.Handle}' has no attribute with tag '{op.Target.AttributeTag}'.";
                    operationResults.Add(new OperationValidationResult(
                        op.OperationId, ErrorCodes.PreconditionFailed, CurrentValue: null, NewValue: op.Value,
                        Message: $"Attribute tag '{op.Target.AttributeTag}' does not exist on block."));
                    continue;
                }

                var current = targetAttr.TextString;
                // Exact ordinal comparison
                if (!string.Equals(current, op.Precondition.Equals, StringComparison.Ordinal))
                {
                    topErrorCode ??= ErrorCodes.PreconditionFailed;
                    topErrorMessage ??= $"Precondition failed for attribute '{op.Target.AttributeTag}' on block '{op.Target.Handle}': expected '{op.Precondition.Equals}', actual is '{current}'.";
                    operationResults.Add(new OperationValidationResult(
                        op.OperationId, ErrorCodes.PreconditionFailed, CurrentValue: current, NewValue: op.Value,
                        Message: $"Precondition mismatch: expected '{op.Precondition.Equals}', found '{current}'."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(
                    op.OperationId, "VALID", CurrentValue: current, NewValue: op.Value));
            }
        }

        var isValid = topErrorCode is null;
        return new PlanValidationResult(isValid, operationResults, topErrorCode, topErrorMessage);
    }

    private static bool TryParseHandle(string handleStr, out Handle handle)
    {
        handle = default;
        if (string.IsNullOrWhiteSpace(handleStr)) return false;
        if (!long.TryParse(handleStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;
        handle = new Handle(value);
        return true;
    }

    private static ChangePlan? ParsePlan(RpcRequest request)
    {
        if (request.Parameters is null) return null;
        try
        {
            if (request.Parameters.Value.ValueKind == JsonValueKind.Object)
            {
                if (request.Parameters.Value.TryGetProperty("plan", out var planElement))
                {
                    return JsonSerializer.Deserialize<ChangePlan>(planElement.GetRawText(), JsonDefaults.Options);
                }
                return JsonSerializer.Deserialize<ChangePlan>(request.Parameters.Value.GetRawText(), JsonDefaults.Options);
            }
        }
        catch
        {
            return null;
        }
        return null;
    }
}
