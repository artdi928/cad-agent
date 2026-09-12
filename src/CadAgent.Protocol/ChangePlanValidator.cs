namespace CadAgent.Protocol;

public static class ChangePlanValidator
{
    public const string KindSetDbText = "set_dbtext";
    public const string KindSetBlockAttribute = "set_block_attribute";

    public static PlanValidationResult ValidatePure(ChangePlan plan)
    {
        if (plan is null)
            return new PlanValidationResult(false, [], ErrorCodes.InvalidChangePlan, "Plan cannot be null.");

        if (plan.Version != 1)
            return new PlanValidationResult(false, [], ErrorCodes.InvalidChangePlan, $"Unsupported change plan version: {plan.Version}. Expected 1.");

        if (plan.Drawing is null || string.IsNullOrWhiteSpace(plan.Drawing.FullPath))
            return new PlanValidationResult(false, [], ErrorCodes.InvalidChangePlan, "Drawing fullPath is required.");

        if (plan.Operations is null || plan.Operations.Count == 0)
            return new PlanValidationResult(false, [], ErrorCodes.InvalidChangePlan, "Plan must contain at least one operation.");

        var seenOpIds = new HashSet<string>(StringComparer.Ordinal);
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operationResults = new List<OperationValidationResult>();
        string? topErrorCode = null;
        string? topErrorMessage = null;

        foreach (var op in plan.Operations)
        {
            if (op is null)
            {
                topErrorCode ??= ErrorCodes.InvalidChangePlan;
                topErrorMessage ??= "Operation cannot be null.";
                operationResults.Add(new OperationValidationResult("unknown", ErrorCodes.InvalidChangePlan, Message: "Operation is null."));
                continue;
            }

            var opId = op.OperationId ?? "unknown";

            if (string.IsNullOrWhiteSpace(op.OperationId))
            {
                topErrorCode ??= ErrorCodes.InvalidChangePlan;
                topErrorMessage ??= "operationId is required.";
                operationResults.Add(new OperationValidationResult("unknown", ErrorCodes.InvalidChangePlan, Message: "operationId is required."));
                continue;
            }

            if (!seenOpIds.Add(op.OperationId))
            {
                topErrorCode ??= ErrorCodes.InvalidChangePlan;
                topErrorMessage ??= $"Duplicate operationId '{op.OperationId}'.";
                operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: $"Duplicate operationId '{op.OperationId}'."));
                continue;
            }

            if (op.Target is null || string.IsNullOrWhiteSpace(op.Target.Handle))
            {
                topErrorCode ??= ErrorCodes.InvalidChangePlan;
                topErrorMessage ??= $"Target handle is required for operation '{op.OperationId}'.";
                operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Target handle is required."));
                continue;
            }

            if (op.Precondition is null || op.Precondition.Equals is null)
            {
                topErrorCode ??= ErrorCodes.InvalidChangePlan;
                topErrorMessage ??= $"Precondition with 'equals' string is required for operation '{op.OperationId}'.";
                operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Precondition.equals is required."));
                continue;
            }

            if (op.Value is null)
            {
                topErrorCode ??= ErrorCodes.InvalidChangePlan;
                topErrorMessage ??= $"Value is required for operation '{op.OperationId}'.";
                operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Value is required."));
                continue;
            }

            string targetKey;
            if (string.Equals(op.Kind, KindSetDbText, StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(op.Target.AttributeTag))
                {
                    topErrorCode ??= ErrorCodes.UnsupportedField;
                    topErrorMessage ??= $"attributeTag is not supported for {KindSetDbText} in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.UnsupportedField, Message: "attributeTag is not supported for set_dbtext."));
                    continue;
                }
                targetKey = $"{op.Target.Handle.Trim()}:{KindSetDbText}";
            }
            else if (string.Equals(op.Kind, KindSetBlockAttribute, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(op.Target.AttributeTag))
                {
                    topErrorCode ??= ErrorCodes.UnsupportedField;
                    topErrorMessage ??= $"attributeTag is required for {KindSetBlockAttribute} in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.UnsupportedField, Message: "attributeTag is required for set_block_attribute."));
                    continue;
                }
                targetKey = $"{op.Target.Handle.Trim()}:attr:{op.Target.AttributeTag.Trim()}";
            }
            else
            {
                topErrorCode ??= ErrorCodes.UnsupportedOperation;
                topErrorMessage ??= $"Unsupported operation kind '{op.Kind}' in operation '{op.OperationId}'.";
                operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.UnsupportedOperation, Message: $"Unsupported operation kind '{op.Kind}'."));
                continue;
            }

            if (!seenTargets.Add(targetKey))
            {
                topErrorCode ??= ErrorCodes.DuplicateTarget;
                topErrorMessage ??= $"Duplicate target '{targetKey}' in operation '{op.OperationId}'.";
                operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.DuplicateTarget, Message: $"Duplicate target '{targetKey}'."));
                continue;
            }

            operationResults.Add(new OperationValidationResult(op.OperationId, "VALID", null, op.Value));
        }

        var isValid = topErrorCode is null;
        return new PlanValidationResult(isValid, operationResults, topErrorCode, topErrorMessage);
    }
}
