namespace CadAgent.Protocol;

public static class ChangePlanValidator
{
    public const string KindSetDbText = "set_dbtext";
    public const string KindSetBlockAttribute = "set_block_attribute";
    public const string KindCreateLine = "create_line";
    public const string KindCreatePolyline = "create_polyline";
    public const string KindCreateCircle = "create_circle";
    public const string KindCreateArc = "create_arc";
    public const string KindCreateDbText = "create_dbtext";
    public const string KindCreateMText = "create_mtext";
    public const string KindInsertBlock = "insert_block";

    private static readonly HashSet<string> ValidHorizontalAlignments = new(StringComparer.OrdinalIgnoreCase)
    {
        "left", "center", "right", "aligned", "middle", "fit"
    };

    private static readonly HashSet<string> ValidVerticalAlignments = new(StringComparer.OrdinalIgnoreCase)
    {
        "baseline", "bottom", "middle", "top"
    };

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

            if (op.Layer is not null && string.IsNullOrWhiteSpace(op.Layer))
            {
                topErrorCode ??= ErrorCodes.InvalidChangePlan;
                topErrorMessage ??= $"Layer cannot be empty or whitespace in operation '{op.OperationId}'.";
                operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Layer cannot be empty or whitespace."));
                continue;
            }

            // Mutation operations
            if (string.Equals(op.Kind, KindSetDbText, StringComparison.Ordinal) ||
                string.Equals(op.Kind, KindSetBlockAttribute, StringComparison.Ordinal))
            {
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
                else
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

                if (!seenTargets.Add(targetKey))
                {
                    topErrorCode ??= ErrorCodes.DuplicateTarget;
                    topErrorMessage ??= $"Duplicate target '{targetKey}' in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.DuplicateTarget, Message: $"Duplicate target '{targetKey}'."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(op.OperationId, "VALID", null, op.Value));
                continue;
            }

            // Creation primitives
            if (string.Equals(op.Kind, KindCreateLine, StringComparison.Ordinal))
            {
                if (!IsFinite(op.Start) || !IsFinite(op.End))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Start and End points with finite coordinates are required for create_line in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Start and End points with finite coordinates are required."));
                    continue;
                }

                if (IsPointsEqual(op.Start!, op.End!))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Start and End points must be distinct for create_line in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Start and End points must be distinct."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(op.OperationId, "VALID"));
            }
            else if (string.Equals(op.Kind, KindCreatePolyline, StringComparison.Ordinal))
            {
                if (op.Points is null || op.Points.Count < 2 || !op.Points.All(IsFinite))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"At least 2 points with finite coordinates are required for create_polyline in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "At least 2 points with finite coordinates are required."));
                    continue;
                }

                if (op.Closed == true)
                {
                    if (op.Points.Count < 3)
                    {
                        topErrorCode ??= ErrorCodes.InvalidChangePlan;
                        topErrorMessage ??= $"At least 3 points are required for a closed polyline in operation '{op.OperationId}'.";
                        operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "At least 3 points are required for a closed polyline."));
                        continue;
                    }

                    var distinctPoints = new HashSet<(double X, double Y)>();
                    foreach (var pt in op.Points)
                        distinctPoints.Add((Round(pt.X), Round(pt.Y)));

                    if (distinctPoints.Count < 3)
                    {
                        topErrorCode ??= ErrorCodes.InvalidChangePlan;
                        topErrorMessage ??= $"Closed polyline requires at least 3 distinct points in operation '{op.OperationId}'.";
                        operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Closed polyline requires at least 3 distinct points."));
                        continue;
                    }
                }

                // Check for non-zero total length
                double totalLength = 0.0;
                for (int i = 0; i < op.Points.Count - 1; i++)
                {
                    var dx = op.Points[i + 1].X - op.Points[i].X;
                    var dy = op.Points[i + 1].Y - op.Points[i].Y;
                    var dz = op.Points[i + 1].Z - op.Points[i].Z;
                    totalLength += Math.Sqrt(dx * dx + dy * dy + dz * dz);
                }

                if (totalLength < 1e-6)
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Polyline cannot have zero length in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Polyline cannot have zero length."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(op.OperationId, "VALID"));
            }
            else if (string.Equals(op.Kind, KindCreateCircle, StringComparison.Ordinal))
            {
                if (!IsFinite(op.Center))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Center point with finite coordinates is required for create_circle in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Center point with finite coordinates is required."));
                    continue;
                }

                if (!op.Radius.HasValue || !double.IsFinite(op.Radius.Value) || op.Radius.Value <= 0.0)
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Radius must be greater than 0 for create_circle in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Radius must be greater than 0."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(op.OperationId, "VALID"));
            }
            else if (string.Equals(op.Kind, KindCreateArc, StringComparison.Ordinal))
            {
                if (!IsFinite(op.Center))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Center point with finite coordinates is required for create_arc in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Center point with finite coordinates is required."));
                    continue;
                }

                if (!op.Radius.HasValue || !double.IsFinite(op.Radius.Value) || op.Radius.Value <= 0.0)
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Radius must be greater than 0 for create_arc in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Radius must be greater than 0."));
                    continue;
                }

                if (!op.StartAngle.HasValue || !double.IsFinite(op.StartAngle.Value) ||
                    !op.EndAngle.HasValue || !double.IsFinite(op.EndAngle.Value))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Finite startAngle and endAngle are required for create_arc in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Finite startAngle and endAngle are required."));
                    continue;
                }

                if (Math.Abs(op.StartAngle.Value - op.EndAngle.Value) < 1e-6)
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"startAngle and endAngle must not be equal for create_arc in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "startAngle and endAngle must not be equal."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(op.OperationId, "VALID"));
            }
            else if (string.Equals(op.Kind, KindCreateDbText, StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(op.Text))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Text must not be empty for create_dbtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Text must not be empty."));
                    continue;
                }

                if (!IsFinite(op.Position))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Position with finite coordinates is required for create_dbtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Position with finite coordinates is required."));
                    continue;
                }

                if (!op.Height.HasValue || !double.IsFinite(op.Height.Value) || op.Height.Value <= 0.0)
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Height must be greater than 0 for create_dbtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Height must be greater than 0."));
                    continue;
                }

                if (op.Rotation.HasValue && !double.IsFinite(op.Rotation.Value))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Rotation must be finite for create_dbtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Rotation must be finite."));
                    continue;
                }

                if (op.HorizontalAlignment is not null && !ValidHorizontalAlignments.Contains(op.HorizontalAlignment))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Unsupported horizontalAlignment '{op.HorizontalAlignment}' in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: $"Unsupported horizontalAlignment '{op.HorizontalAlignment}'."));
                    continue;
                }

                if (op.VerticalAlignment is not null && !ValidVerticalAlignments.Contains(op.VerticalAlignment))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Unsupported verticalAlignment '{op.VerticalAlignment}' in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: $"Unsupported verticalAlignment '{op.VerticalAlignment}'."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(op.OperationId, "VALID"));
            }
            else if (string.Equals(op.Kind, KindCreateMText, StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(op.Text))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Text must not be empty for create_mtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Text must not be empty."));
                    continue;
                }

                if (!IsFinite(op.Position))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Position with finite coordinates is required for create_mtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Position with finite coordinates is required."));
                    continue;
                }

                if (!op.TextHeight.HasValue || !double.IsFinite(op.TextHeight.Value) || op.TextHeight.Value <= 0.0)
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"TextHeight must be greater than 0 for create_mtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "TextHeight must be greater than 0."));
                    continue;
                }

                if (op.Width.HasValue && (!double.IsFinite(op.Width.Value) || op.Width.Value < 0.0))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Width must be non-negative for create_mtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Width must be non-negative."));
                    continue;
                }

                if (op.Rotation.HasValue && !double.IsFinite(op.Rotation.Value))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Rotation must be finite for create_mtext in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Rotation must be finite."));
                    continue;
                }

                operationResults.Add(new OperationValidationResult(op.OperationId, "VALID"));
            }
            else if (string.Equals(op.Kind, KindInsertBlock, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(op.BlockName))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"BlockName is required for insert_block in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "BlockName is required."));
                    continue;
                }

                if (!IsFinite(op.Position))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Position with finite coordinates is required for insert_block in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Position with finite coordinates is required."));
                    continue;
                }

                if (op.Rotation.HasValue && !double.IsFinite(op.Rotation.Value))
                {
                    topErrorCode ??= ErrorCodes.InvalidChangePlan;
                    topErrorMessage ??= $"Rotation must be finite for insert_block in operation '{op.OperationId}'.";
                    operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Rotation must be finite."));
                    continue;
                }

                if (op.Scale is not null)
                {
                    if (!double.IsFinite(op.Scale.X) || !double.IsFinite(op.Scale.Y) || !double.IsFinite(op.Scale.Z) ||
                        Math.Abs(op.Scale.X) < 1e-9 || Math.Abs(op.Scale.Y) < 1e-9 || Math.Abs(op.Scale.Z) < 1e-9)
                    {
                        topErrorCode ??= ErrorCodes.InvalidChangePlan;
                        topErrorMessage ??= $"Scale factors must be non-zero finite values in operation '{op.OperationId}'.";
                        operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Scale factors must be non-zero finite values."));
                        continue;
                    }
                }

                if (op.Attributes is not null)
                {
                    bool hasEmptyKey = op.Attributes.Keys.Any(string.IsNullOrWhiteSpace);
                    if (hasEmptyKey)
                    {
                        topErrorCode ??= ErrorCodes.InvalidChangePlan;
                        topErrorMessage ??= $"Attribute tags must not be empty in operation '{op.OperationId}'.";
                        operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.InvalidChangePlan, Message: "Attribute tags must not be empty."));
                        continue;
                    }
                }

                operationResults.Add(new OperationValidationResult(op.OperationId, "VALID"));
            }
            else
            {
                topErrorCode ??= ErrorCodes.UnsupportedOperation;
                topErrorMessage ??= $"Unsupported operation kind '{op.Kind}' in operation '{op.OperationId}'.";
                operationResults.Add(new OperationValidationResult(op.OperationId, ErrorCodes.UnsupportedOperation, Message: $"Unsupported operation kind '{op.Kind}'."));
            }
        }

        var isValid = topErrorCode is null;
        return new PlanValidationResult(isValid, operationResults, topErrorCode, topErrorMessage);
    }

    private static bool IsFinite(PointDto? pt) =>
        pt is not null && double.IsFinite(pt.X) && double.IsFinite(pt.Y) && double.IsFinite(pt.Z);

    private static bool IsPointsEqual(PointDto a, PointDto b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 && Math.Abs(a.Z - b.Z) < 1e-9;

    private static double Round(double val) => Math.Round(val, 6);
}
