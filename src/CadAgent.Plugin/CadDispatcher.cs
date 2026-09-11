using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CadAgent.Protocol;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadAgent.Plugin;

internal sealed class CadDispatcher
{
    public Task<RpcResponse> DispatchAsync(RpcRequest request)
    {
        if (request.Version != ProtocolConstants.Version)
            return Task.FromResult(RpcResponse.Failure(request.RequestId, "protocol_mismatch", "Unsupported protocol version."));
        if (string.IsNullOrWhiteSpace(request.RequestId))
            return Task.FromResult(RpcResponse.Failure("unknown", "invalid_request", "requestId is required."));
        if (!ProtocolConstants.AllowedMethods.Contains(request.Method))
            return Task.FromResult(RpcResponse.Failure(request.RequestId, "unknown_method", "Method is not available in the B1 read-only contract."));

        var completion = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // ExecuteInApplicationContext is the explicit AutoCAD main-thread boundary.
            AcApplication.DocumentManager.ExecuteInApplicationContext(_ =>
            {
                try { completion.TrySetResult(DispatchOnAutoCadThread(request)); }
                catch (Exception exception)
                {
                    completion.TrySetResult(RpcResponse.Failure(request.RequestId, "cad_error", exception.Message));
                }
            }, null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(RpcResponse.Failure(request.RequestId, "cad_context_unavailable", exception.Message));
        }
        return completion.Task;
    }

    private static RpcResponse DispatchOnAutoCadThread(RpcRequest request) => request.Method switch
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

    private static RpcResponse WithDocument(string requestId, Func<Document, object> read)
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        return document is null
            ? RpcResponse.Failure(requestId, "no_active_document", "AutoCAD has no active document.")
            : RpcResponse.Success(requestId, read(document));
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
        return table.Cast<ObjectId>()
            .Select(id => (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead))
            .Select(block => new
            {
                block.Name,
                handle = block.Handle.ToString(),
                isAnonymous = block.IsAnonymous,
                isLayout = block.IsLayout,
                isFromExternalReference = block.IsFromExternalReference
            })
            .OrderBy(block => block.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
