using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CadAgent.Protocol;

namespace CadAgent.Plugin;

internal static class EntityInspector
{
    private static readonly HashSet<string> DefaultTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Line", "Polyline", "Circle", "Arc", "DBText", "MText", "MLeader", "BlockReference"
    };

    public static IReadOnlyList<EntityDto> ListEntities(Document document, IReadOnlyList<string>? requestedTypes)
    {
        var filter = requestedTypes is { Count: > 0 }
            ? new HashSet<string>(requestedTypes.Select(NormalizeTypeName), StringComparer.OrdinalIgnoreCase)
            : DefaultTypes;

        using var transaction = document.TransactionManager.StartOpenCloseTransaction();
        var table = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        var result = new List<EntityDto>();

        foreach (var spaceId in table.Cast<ObjectId>())
        {
            var space = (BlockTableRecord)transaction.GetObject(spaceId, OpenMode.ForRead);
            if (!space.IsLayout) continue;

            var isModel = string.Equals(space.Name, BlockTableRecord.ModelSpace, StringComparison.OrdinalIgnoreCase);
            string spaceName;
            string layoutName;

            if (isModel)
            {
                spaceName = "model";
                layoutName = "Model";
            }
            else
            {
                spaceName = "paper";
                layoutName = "Layout";
                if (space.LayoutId.IsValid)
                {
                    if (transaction.GetObject(space.LayoutId, OpenMode.ForRead) is Layout layout &&
                        !string.IsNullOrWhiteSpace(layout.LayoutName))
                    {
                        layoutName = layout.LayoutName;
                    }
                }
            }

            foreach (var id in space)
            {
                var entityObj = transaction.GetObject(id, OpenMode.ForRead);
                if (entityObj is not Entity entity) continue;

                // AttributeReference must not be listed as top-level generic entity.
                if (entity is AttributeReference) continue;

                var handle = entity.Handle.ToString();
                var layer = entity.Layer;

                if (entity is Line line && filter.Contains("Line"))
                {
                    result.Add(new EntityDto(
                        Handle: handle,
                        EntityType: "Line",
                        Layer: layer,
                        Space: spaceName,
                        Layout: layoutName,
                        Start: new PositionDto(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
                        End: new PositionDto(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z)));
                }
                else if (entity is Polyline poly && filter.Contains("Polyline"))
                {
                    var vertices = new List<PositionDto>();
                    for (int i = 0; i < poly.NumberOfVertices; i++)
                    {
                        var pt = poly.GetPoint2dAt(i);
                        vertices.Add(new PositionDto(pt.X, pt.Y, 0));
                    }
                    result.Add(new EntityDto(
                        Handle: handle,
                        EntityType: "Polyline",
                        Layer: layer,
                        Space: spaceName,
                        Layout: layoutName,
                        Vertices: vertices,
                        Closed: poly.Closed));
                }
                else if (entity is Circle circle && filter.Contains("Circle"))
                {
                    result.Add(new EntityDto(
                        Handle: handle,
                        EntityType: "Circle",
                        Layer: layer,
                        Space: spaceName,
                        Layout: layoutName,
                        Center: new PositionDto(circle.Center.X, circle.Center.Y, circle.Center.Z),
                        Radius: circle.Radius));
                }
                else if (entity is Arc arc && filter.Contains("Arc"))
                {
                    result.Add(new EntityDto(
                        Handle: handle,
                        EntityType: "Arc",
                        Layer: layer,
                        Space: spaceName,
                        Layout: layoutName,
                        Center: new PositionDto(arc.Center.X, arc.Center.Y, arc.Center.Z),
                        Radius: arc.Radius,
                        StartAngle: arc.StartAngle,
                        EndAngle: arc.EndAngle));
                }
                else if (entity is DBText dbText && filter.Contains("DBText"))
                {
                    var isAligned = dbText.HorizontalMode != TextHorizontalMode.TextLeft || dbText.VerticalMode != TextVerticalMode.TextBase;
                    var anchor = isAligned ? new PositionDto(dbText.AlignmentPoint.X, dbText.AlignmentPoint.Y, dbText.AlignmentPoint.Z) : null;

                    result.Add(new EntityDto(
                        Handle: handle,
                        EntityType: "DBText",
                        Layer: layer,
                        Space: spaceName,
                        Layout: layoutName,
                        Position: new PositionDto(dbText.Position.X, dbText.Position.Y, dbText.Position.Z),
                        Anchor: anchor,
                        Height: dbText.Height,
                        Rotation: dbText.Rotation,
                        Alignment: $"{dbText.HorizontalMode}/{dbText.VerticalMode}",
                        Text: dbText.TextString));
                }
                else if (entity is MText mText && filter.Contains("MText"))
                {
                    result.Add(new EntityDto(
                        Handle: handle,
                        EntityType: "MText",
                        Layer: layer,
                        Space: spaceName,
                        Layout: layoutName,
                        Location: new PositionDto(mText.Location.X, mText.Location.Y, mText.Location.Z),
                        TextHeight: mText.TextHeight,
                        Width: mText.Width,
                        Rotation: mText.Rotation,
                        PlainText: mText.Text,
                        RawContents: mText.Contents));
                }
                else if (entity is MLeader mLeader && filter.Contains("MLeader"))
                {
                    var isMTextContent = mLeader.ContentType == ContentType.MTextContent;
                    PositionDto? anchor = null;
                    string? plainText = null;
                    string? rawContents = null;

                    if (isMTextContent)
                    {
                        var textLocation = mLeader.TextLocation;
                        anchor = new PositionDto(textLocation.X, textLocation.Y, textLocation.Z);
                        var mt = mLeader.MText;
                        if (mt is not null)
                        {
                            plainText = mt.Text;
                            rawContents = mt.Contents;
                        }
                    }

                    result.Add(new EntityDto(
                        Handle: handle,
                        EntityType: "MLeader",
                        Layer: layer,
                        Space: spaceName,
                        Layout: layoutName,
                        Anchor: anchor,
                        PlainText: plainText,
                        RawContents: rawContents,
                        ContentType: mLeader.ContentType.ToString()));
                }
                else if (entity is BlockReference block && filter.Contains("BlockReference"))
                {
                    result.Add(new EntityDto(
                        Handle: handle,
                        EntityType: "BlockReference",
                        Layer: layer,
                        Space: spaceName,
                        Layout: layoutName,
                        Position: new PositionDto(block.Position.X, block.Position.Y, block.Position.Z),
                        Rotation: block.Rotation,
                        Scale: new ScaleDto(block.ScaleFactors.X, block.ScaleFactors.Y, block.ScaleFactors.Z),
                        EffectiveName: EffectiveName(block, transaction),
                        Attributes: ReadAttributes(block, transaction)));
                }
            }
        }

        return result.OrderBy(e => e.Handle, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeTypeName(string type) => type.Trim() switch
    {
        var t when string.Equals(t, "block", StringComparison.OrdinalIgnoreCase) => "BlockReference",
        var t when string.Equals(t, "text", StringComparison.OrdinalIgnoreCase) => "DBText",
        var t when string.Equals(t, "line", StringComparison.OrdinalIgnoreCase) => "Line",
        var t when string.Equals(t, "polyline", StringComparison.OrdinalIgnoreCase) => "Polyline",
        var t when string.Equals(t, "circle", StringComparison.OrdinalIgnoreCase) => "Circle",
        var t when string.Equals(t, "arc", StringComparison.OrdinalIgnoreCase) => "Arc",
        var t => t
    };

    private static string EffectiveName(BlockReference block, Transaction transaction)
    {
        var id = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
        return ((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name;
    }

    private static SortedDictionary<string, string> ReadAttributes(BlockReference block, Transaction transaction)
    {
        var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (ObjectId id in block.AttributeCollection)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is AttributeReference attribute)
                attributes[attribute.Tag] = attribute.TextString;
        }
        return attributes;
    }
}
