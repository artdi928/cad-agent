using System.IO.Pipes;
using System.Text.Json;
using CadAgent.Protocol;

namespace CadAgent.Bridge;

public sealed class PipeClient(string pipeName = ProtocolConstants.DefaultPipeName, TimeSpan? timeout = null)
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(10);

    public async Task<RpcResponse> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(linked.Token).ConfigureAwait(false);
            await PipeFraming.WriteAsync(pipe,
                new RpcRequest(ProtocolConstants.Version, requestId, method, parameters), linked.Token).ConfigureAwait(false);
            var response = await PipeFraming.ReadAsync<RpcResponse>(pipe, linked.Token).ConfigureAwait(false)
                ?? throw new IOException("Plugin closed the pipe before responding.");
            if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                throw new InvalidDataException("Response requestId does not match the request.");
            return response;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Request '{method}' exceeded {_timeout.TotalMilliseconds:0} ms.");
        }
    }
}
