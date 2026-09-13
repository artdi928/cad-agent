using System.Text.Json.Serialization;

namespace CadAgent.Protocol;

public sealed record ChangePlan(
    int Version,
    DrawingTarget Drawing,
    IReadOnlyList<PlanOperation> Operations);

public sealed record DrawingTarget(
    string FullPath);

public sealed record PointDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("z")] double Z = 0.0);

public sealed record ScaleDto(
    [property: JsonPropertyName("x")] double X = 1.0,
    [property: JsonPropertyName("y")] double Y = 1.0,
    [property: JsonPropertyName("z")] double Z = 1.0);

public sealed record PlanOperation(
    string OperationId,
    string Kind,
    OperationTarget? Target = null,
    Precondition? Precondition = null,
    string? Value = null,
    string? Layer = null,
    PointDto? Start = null,
    PointDto? End = null,
    IReadOnlyList<PointDto>? Points = null,
    bool? Closed = null,
    PointDto? Center = null,
    double? Radius = null,
    double? StartAngle = null,
    double? EndAngle = null,
    string? Text = null,
    PointDto? Position = null,
    double? Height = null,
    double? TextHeight = null,
    double? Width = null,
    double? Rotation = null,
    string? HorizontalAlignment = null,
    string? VerticalAlignment = null,
    string? BlockName = null,
    ScaleDto? Scale = null,
    IReadOnlyDictionary<string, string>? Attributes = null
#if DEBUG
    , [property: JsonPropertyName("faultInjection")]
    string? FaultInjection = null
#endif
);

public sealed record OperationTarget(
    string Handle,
    string? AttributeTag = null);

public sealed class Precondition
{
    [JsonPropertyName("equals")]
    public new string Equals { get; init; } = "";

    public Precondition() { }
    public Precondition(string equals)
    {
        Equals = equals;
    }
}

public sealed record PlanValidationResult(
    bool Valid,
    IReadOnlyList<OperationValidationResult> Operations,
    string? ErrorCode = null,
    string? Message = null);

public sealed record OperationValidationResult(
    string OperationId,
    string Status,
    string? CurrentValue = null,
    string? NewValue = null,
    string? Message = null);

public sealed record PlanApplyResult(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    bool Applied,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    bool? PostconditionVerifiedBeforeCommit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    bool? RollbackVerified,
    IReadOnlyList<OperationApplyResult>? Operations = null);

public sealed record OperationApplyResult(
    string OperationId,
    string Status,
    string? PreviousValue = null,
    string? NewValue = null,
    string? Handle = null,
    string? EntityType = null,
    IReadOnlyDictionary<string, object?>? Properties = null);
