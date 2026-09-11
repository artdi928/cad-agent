using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadAgent.Protocol;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadAgent.Plugin;

internal sealed class CadDispatcher
{
    // Called only by PluginRuntime's AutoCAD Application.Idle handler.
    public RpcResponse Dispatch(RpcRequest request)
    {
        try
        {
            return request.Method switch
            {
                "system.ping" => RpcResponse.Success(request.RequestId, new
                {
                    pong = true,
                    protocolVersion = ProtocolConstants.Version,
                    processId = Environment.ProcessId
                }),
                "cad.get_drawing_info" => WithDocument(request.RequestId, DrawingInfo),
                "cad.list_layers" => WithDocument(request.RequestId, ListLayers),
                "cad.list_blocks" => WithDocument(request.RequestId, ListBlocks),
                _ => RpcResponse.Failure(request.RequestId, "unknown_method", "Method is not available.")
            };
        }
        catch (Exception exception)
        {
            PluginRuntime.RecordError(exception);
            return RpcResponse.Failure(request.RequestId, "cad_error", exception.Message);
        }
    }

    private static RpcResponse WithDocument(string requestId, Func<Document, object> read)
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
            return RpcResponse.Failure(requestId, "no_active_document", "AutoCAD has no active document.");

        using (document.LockDocument())
            return RpcResponse.Success(requestId, read(document));
    }

    private static object DrawingInfo(Document document)
    {
        using var transaction = document.TransactionManager.StartOpenCloseTransaction();
        var database = document.Database;
        var layer = (LayerTableRecord)transaction.GetObject(database.Clayer, OpenMode.ForRead);
        return new
        {
            name = document.Name,
            fileName = database.Filename,
            currentLayer = layer.Name,
            measurement = database.Measurement.ToString(),
            insertionUnits = database.Insunits.ToString()
        };
    }

    private static object ListLayers(Document document)
    {
        using var transaction = document.TransactionManager.StartOpenCloseTransaction();
        var table = (LayerTable)transaction.GetObject(document.Database.LayerTableId, OpenMode.ForRead);
        return table.Cast<ObjectId>()
            .Select(id => (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead))
            .Select(layer => new
            {
                layer.Name,
                handle = layer.Handle.ToString(),
                isOff = layer.IsOff,
                isFrozen = layer.IsFrozen,
                isLocked = layer.IsLocked,
                isPlottable = layer.IsPlottable
            })
            .OrderBy(layer => layer.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static object ListBlocks(Document document)
    {
        using var transaction = document.TransactionManager.StartOpenCloseTransaction();
        var table = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        var result = new List<BlockInstance>();

        foreach (var space in table.Cast<ObjectId>()
                     .Select(id => (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead))
                     .Where(record => record.IsLayout))
        {
            var spaceName = string.Equals(space.Name, BlockTableRecord.ModelSpace, StringComparison.Ordinal)
                ? "model"
                : "paper";
            foreach (var id in space)
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is not BlockReference block) continue;
                result.Add(new BlockInstance(
                    block.Handle.ToString(), EffectiveName(block, transaction), block.Layer, spaceName,
                    new Position(block.Position.X, block.Position.Y, block.Position.Z),
                    Attributes(block, transaction)));
            }
        }

        return result.OrderBy(block => block.Handle, StringComparer.Ordinal).ToArray();
    }

    private static string EffectiveName(BlockReference block, Transaction transaction)
    {
        var id = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
        return ((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name;
    }

    private static SortedDictionary<string, string> Attributes(BlockReference block, Transaction transaction)
    {
        var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (ObjectId id in block.AttributeCollection)
            if (transaction.GetObject(id, OpenMode.ForRead) is AttributeReference attribute)
                attributes[attribute.Tag] = attribute.TextString;
        return attributes;
    }

    private sealed record BlockInstance(
        string Handle, string Name, string Layer, string Space, Position Position,
        SortedDictionary<string, string> Attributes);
    private sealed record Position(double X, double Y, double Z);
}
