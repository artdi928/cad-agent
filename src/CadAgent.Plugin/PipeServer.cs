using System.IO.Pipes;
using System.Text.Json;
using CadAgent.Protocol;

namespace CadAgent.Plugin;

internal sealed class PipeServer(string pipeName, CadDispatcher dispatcher) : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _listener;
    public string PipeName => pipeName;

    public void Start() => _listener = Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
                var request = await PipeFraming.ReadAsync<RpcRequest>(pipe, _stopping.Token).ConfigureAwait(false);
                if (request is null) continue;
                var response = await dispatcher.DispatchAsync(request).ConfigureAwait(false);
                await PipeFraming.WriteAsync(pipe, response, _stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.WriteLine($"CadAgent pipe session failed: {exception}");
            }
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _stopping.Dispose();
    }
}
