using System.IO.Pipes;
using System.Text.Json;
using CadAgent.Protocol;

namespace CadAgent.Plugin;

internal sealed class PipeServer(string pipeName, CadRequestPump pump) : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _listener;
    public string PipeName => pipeName;

    public void Start()
    {
        _listener = Task.Run(ListenAsync);
        Trace("pipe listener started");
    }

    private async Task ListenAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
                Trace("client connected");
                await ServeAsync(pipe).ConfigureAwait(false);
                Trace("client disconnected");
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                PluginRuntime.RecordError(exception);
                Trace("request failed");
            }
        }
    }

    private async Task ServeAsync(Stream pipe)
    {
        RpcRequest request;
        try
        {
            request = await PipeFraming.ReadAsync<RpcRequest>(pipe, _stopping.Token).ConfigureAwait(false)
                ?? throw new JsonException("Request body is empty.");
        }
        catch (JsonException exception)
        {
            Trace($"malformed request: {exception.Message}");
            await PipeFraming.WriteAsync(pipe,
                RpcResponse.Failure("unknown", "invalid_request", "Request JSON is malformed."),
                _stopping.Token).ConfigureAwait(false);
            return;
        }

        Trace($"request accepted id={request.RequestId} method={request.Method}");
        var validationError = ProtocolValidation.Validate(request);
        if (validationError is not null)
        {
            await PipeFraming.WriteAsync(pipe, validationError, _stopping.Token).ConfigureAwait(false);
            Trace($"request failed id={request.RequestId} code={validationError.Error?.Code}");
            return;
        }

        var completion = pump.Enqueue(request, CancellationToken.None);
        if (completion.IsCompleted) Trace($"request rejected id={request.RequestId} queue overflow or plugin stopping");
        else Trace($"request queued id={request.RequestId}");
        var response = await completion.ConfigureAwait(false);
        await PipeFraming.WriteAsync(pipe, response, _stopping.Token).ConfigureAwait(false);
        Trace(response.Ok
            ? $"request completed id={request.RequestId}"
            : $"request failed id={request.RequestId} code={response.Error?.Code}");
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _stopping.Dispose();
    }

    private static void Trace(string message) =>
        System.Diagnostics.Trace.WriteLine($"CadAgent {DateTimeOffset.UtcNow:O} {message}");
}
