using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadAgent.Protocol;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const int MaximumMessageBytes = 1024 * 1024;
    public const string DefaultPipeName = "cad-agent-b1";
    public static readonly IReadOnlySet<string> AllowedMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "system.ping", "cad.get_drawing_info", "cad.list_layers", "cad.list_blocks"
    };
}

public sealed record RpcRequest(int Version, string RequestId, string Method, JsonElement? Parameters = null);
public sealed record RpcResponse(int Version, string RequestId, bool Ok, JsonElement? Result = null, RpcError? Error = null)
{
    public static RpcResponse Success(string id, object value) =>
        new(ProtocolConstants.Version, id, true, JsonSerializer.SerializeToElement(value, JsonDefaults.Options));
    public static RpcResponse Failure(string id, string code, string message) =>
        new(ProtocolConstants.Version, id, false, null, new RpcError(code, message));
}
public sealed record RpcError(string Code, string Message);

public static class ProtocolValidation
{
    public static RpcResponse? Validate(RpcRequest request)
    {
        if (request.Version != ProtocolConstants.Version)
            return RpcResponse.Failure(request.RequestId ?? "unknown", "protocol_mismatch", "Unsupported protocol version.");
        if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.Method))
            return RpcResponse.Failure("unknown", "invalid_request", "requestId and method are required.");
        return ProtocolConstants.AllowedMethods.Contains(request.Method)
            ? null
            : RpcResponse.Failure(request.RequestId, "unknown_method", "Method is not available in the B1 read-only contract.");
    }
}

public sealed class CadRequestPump
{
    private readonly object _sync = new();
    private readonly Queue<PendingRequest> _queue = new();
    private readonly int _capacity;
    private bool _stopping;

    public CadRequestPump(int capacity = 32)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int PendingCount { get { lock (_sync) return _queue.Count; } }

    public Task<RpcResponse> Enqueue(RpcRequest request, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_stopping)
                return Task.FromResult(RpcResponse.Failure(request.RequestId, "plugin_stopping", "Plugin is stopping."));
            if (_queue.Count >= _capacity)
                return Task.FromResult(RpcResponse.Failure(request.RequestId, "server_busy", "Request queue is full."));
            var pending = new PendingRequest(request, cancellationToken);
            _queue.Enqueue(pending);
            return pending.Task;
        }
    }

    public int Drain(int maximum, Func<RpcRequest, RpcResponse> dispatch)
    {
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        var handled = 0;
        while (handled < maximum)
        {
            PendingRequest pending;
            lock (_sync)
            {
                if (_queue.Count == 0) break;
                pending = _queue.Dequeue();
            }
            if (!pending.IsCompleted)
            {
                try { pending.Complete(dispatch(pending.Request)); }
                catch { pending.Complete(RpcResponse.Failure(pending.Request.RequestId, "internal_error", "Request failed internally.")); }
            }
            handled++;
        }
        return handled;
    }

    public void Stop()
    {
        PendingRequest[] pending;
        lock (_sync)
        {
            if (_stopping) return;
            _stopping = true;
            pending = _queue.ToArray();
            _queue.Clear();
        }
        foreach (var item in pending)
            item.Complete(RpcResponse.Failure(item.Request.RequestId, "plugin_stopping", "Plugin is stopping."));
    }

    private sealed class PendingRequest
    {
        private readonly TaskCompletionSource<RpcResponse> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;

        public PendingRequest(RpcRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            _registration = cancellationToken.Register(() => Complete(
                RpcResponse.Failure(request.RequestId, "request_timeout", "Request was cancelled before execution.")));
            if (_completion.Task.IsCompleted) _registration.Dispose();
        }

        public RpcRequest Request { get; }
        public Task<RpcResponse> Task => _completion.Task;
        public bool IsCompleted => _completion.Task.IsCompleted;
        public void Complete(RpcResponse response)
        {
            if (_completion.TrySetResult(response)) _registration.Dispose();
        }
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

// Adapted from AcadMcp.Shared/Pipe/PipeFraming.cs (MIT); see THIRD_PARTY_NOTICES.md.
public static class PipeFraming
{
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        var headerBytes = await ReadExactAsync(stream, header, allowCleanEof: true, cancellationToken).ConfigureAwait(false);
        if (headerBytes == 0) return default;
        if (headerBytes != header.Length) throw new IOException("Pipe closed during frame header.");

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > ProtocolConstants.MaximumMessageBytes)
            throw new InvalidDataException($"Invalid frame length: {length}.");

        var payload = new byte[length];
        if (await ReadExactAsync(stream, payload, allowCleanEof: false, cancellationToken).ConfigureAwait(false) != length)
            throw new IOException("Pipe closed during frame payload.");

        return JsonSerializer.Deserialize<T>(payload, JsonDefaults.Options)
            ?? throw new JsonException("Frame payload is null.");
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
        if (payload.Length == 0 || payload.Length > ProtocolConstants.MaximumMessageBytes)
            throw new InvalidDataException($"Invalid outbound frame length: {payload.Length}.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, bool allowCleanEof, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), token).ConfigureAwait(false);
            if (read == 0) return allowCleanEof && offset == 0 ? 0 : offset;
            offset += read;
        }
        return offset;
    }
}
