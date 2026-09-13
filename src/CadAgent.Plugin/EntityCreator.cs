using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadAgent.Protocol;

namespace CadAgent.Plugin;

internal static class EntityCreator
{
    public static Entity CreateEntity(
        PlanOperation op,
        Database db,
        Transaction tx,
        BlockTableRecord modelSpace)
    {
        Entity entity;
        ObjectId blockDefId = ObjectId.Null;

        switch (op.Kind)
        {
            case ChangePlanValidator.KindCreateLine:
            {
                var start = new Point3d(op.Start!.X, op.Start.Y, op.Start.Z);
                var end = new Point3d(op.End!.X, op.End.Y, op.End.Z);
                var line = new Line(start, end);
                line.SetDatabaseDefaults(db);
                entity = line;
                break;
            }

            case ChangePlanValidator.KindCreatePolyline:
            {
                var poly = new Polyline();
                poly.SetDatabaseDefaults(db);
                for (int i = 0; i < op.Points!.Count; i++)
                {
                    poly.AddVertexAt(i, new Point2d(op.Points[i].X, op.Points[i].Y), 0, 0, 0);
                }
                poly.Closed = op.Closed == true;
                entity = poly;
                break;
            }

            case ChangePlanValidator.KindCreateCircle:
            {
                var center = new Point3d(op.Center!.X, op.Center.Y, op.Center.Z);
                var circle = new Circle(center, Vector3d.ZAxis, op.Radius!.Value);
                circle.SetDatabaseDefaults(db);
                entity = circle;
                break;
            }

            case ChangePlanValidator.KindCreateArc:
            {
                var center = new Point3d(op.Center!.X, op.Center.Y, op.Center.Z);
                var arc = new Arc(center, Vector3d.ZAxis, op.Radius!.Value, op.StartAngle!.Value, op.EndAngle!.Value);
                arc.SetDatabaseDefaults(db);
                entity = arc;
                break;
            }

            case ChangePlanValidator.KindCreateDbText:
            {
                var dbText = new DBText();
                dbText.SetDatabaseDefaults(db);
                dbText.TextString = op.Text!;
                dbText.Height = op.Height!.Value;
                if (op.Rotation.HasValue) dbText.Rotation = op.Rotation.Value;

                var pos = new Point3d(op.Position!.X, op.Position.Y, op.Position.Z);
                dbText.Position = pos;

                var hAlign = ParseHorizontalAlignment(op.HorizontalAlignment);
                var vAlign = ParseVerticalAlignment(op.VerticalAlignment);
                dbText.HorizontalMode = hAlign;
                dbText.VerticalMode = vAlign;

                if (hAlign != TextHorizontalMode.TextLeft || vAlign != TextVerticalMode.TextBase)
                {
                    dbText.AlignmentPoint = pos;
                    dbText.AdjustAlignment(db);
                }

                entity = dbText;
                break;
            }

            case ChangePlanValidator.KindCreateMText:
            {
                var mText = new MText();
                mText.SetDatabaseDefaults(db);
                mText.Location = new Point3d(op.Position!.X, op.Position.Y, op.Position.Z);
                mText.TextHeight = op.TextHeight!.Value;
                if (op.Width.HasValue && op.Width.Value > 0)
                    mText.Width = op.Width.Value;
                if (op.Rotation.HasValue)
                    mText.Rotation = op.Rotation.Value;
                mText.Contents = op.Text!;
                entity = mText;
                break;
            }

            case ChangePlanValidator.KindInsertBlock:
            {
                var blockTable = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
                blockDefId = blockTable[op.BlockName!];
                var pos = new Point3d(op.Position!.X, op.Position.Y, op.Position.Z);
                var blockRef = new BlockReference(pos, blockDefId);
                blockRef.SetDatabaseDefaults(db);
                if (op.Scale is not null)
                    blockRef.ScaleFactors = new Scale3d(op.Scale.X, op.Scale.Y, op.Scale.Z);
                if (op.Rotation.HasValue)
                    blockRef.Rotation = op.Rotation.Value;
                entity = blockRef;
                break;
            }

            default:
                throw new InvalidOperationException($"Unsupported creation kind: '{op.Kind}'.");
        }

        if (!string.IsNullOrWhiteSpace(op.Layer))
        {
            entity.Layer = op.Layer;
        }

        modelSpace.AppendEntity(entity);
        tx.AddNewlyCreatedDBObject(entity, true);

        if (entity is BlockReference insertedBlock && !blockDefId.IsNull)
        {
            var btr = (BlockTableRecord)tx.GetObject(blockDefId, OpenMode.ForRead);
            if (btr.HasAttributeDefinitions)
            {
                foreach (ObjectId id in btr)
                {
                    if (tx.GetObject(id, OpenMode.ForRead) is AttributeDefinition attDef && !attDef.Constant)
                    {
                        var attRef = new AttributeReference();
                        attRef.SetAttributeFromBlock(attDef, insertedBlock.BlockTransform);
                        if (op.Attributes != null && op.Attributes.TryGetValue(attDef.Tag, out var val))
                        {
                            attRef.TextString = val;
                        }
                        insertedBlock.AttributeCollection.AppendAttribute(attRef);
                        tx.AddNewlyCreatedDBObject(attRef, true);
                    }
                }
            }
        }

        return entity;
    }

    private static TextHorizontalMode ParseHorizontalAlignment(string? align) =>
        align?.ToLowerInvariant() switch
        {
            "center" => TextHorizontalMode.TextCenter,
            "right" => TextHorizontalMode.TextRight,
            "aligned" => TextHorizontalMode.TextAlign,
            "middle" => TextHorizontalMode.TextMid,
            "fit" => TextHorizontalMode.TextFit,
            _ => TextHorizontalMode.TextLeft
        };

    private static TextVerticalMode ParseVerticalAlignment(string? align) =>
        align?.ToLowerInvariant() switch
        {
            "bottom" => TextVerticalMode.TextBottom,
            "middle" => TextVerticalMode.TextVerticalMid,
            "top" => TextVerticalMode.TextTop,
            _ => TextVerticalMode.TextBase
        };
}
