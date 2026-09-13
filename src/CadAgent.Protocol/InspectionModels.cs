namespace CadAgent.Protocol;

public sealed record PositionDto(double X, double Y, double Z);

public sealed record EntityDto(
    string Handle,
    string EntityType,
    string Layer,
    string Space,
    string Layout,
    PositionDto? Position = null,
    PositionDto? Location = null,
    PositionDto? Anchor = null,
    PositionDto? Start = null,
    PositionDto? End = null,
    PositionDto? Center = null,
    double? Radius = null,
    double? StartAngle = null,
    double? EndAngle = null,
    IReadOnlyList<PositionDto>? Vertices = null,
    bool? Closed = null,
    double? Height = null,
    double? TextHeight = null,
    double? Width = null,
    double? Rotation = null,
    ScaleDto? Scale = null,
    string? Alignment = null,
    string? Text = null,
    string? PlainText = null,
    string? RawContents = null,
    string? ContentType = null,
    string? EffectiveName = null,
    SortedDictionary<string, string>? Attributes = null);

public sealed record ListEntitiesParameters(
    IReadOnlyList<string>? Types = null);
