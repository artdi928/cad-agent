using System.Text.Json.Serialization;

namespace CadAgent.Protocol;

public sealed record ChangePlan(
    int Version,
    DrawingTarget Drawing,
    IReadOnlyList<PlanOperation> Operations);

public sealed record DrawingTarget(
    string FullPath);

public sealed record PlanOperation(
    string OperationId,
    string Kind,
    OperationTarget Target,
    Precondition Precondition,
    string Value
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
    string PreviousValue,
    string NewValue);
