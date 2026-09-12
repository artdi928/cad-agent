using System.Text.Json;

namespace CadAgent.Mcp;

public sealed class McpServer(McpToolHandler toolHandler)
{
    private static readonly SemaphoreSlim OutputLock = new(1, 1);

    public async Task RunAsync(TextReader reader, TextWriter writer, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                // Stdio EOF reached
                break;
            }

            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = await ProcessMessageAsync(line, cancellationToken).ConfigureAwait(false);
            if (response is not null)
            {
                var json = JsonSerializer.Serialize(response, McpJsonDefaults.Options);
                await OutputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await writer.WriteLineAsync(json).ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    OutputLock.Release();
                }
            }
        }
    }

    public async Task<JsonRpcResponse?> ProcessMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        message = message.Trim().TrimStart('\uFEFF');
        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(message, McpJsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            return JsonRpcResponse.Failure(null, JsonRpcErrorCodes.ParseError, $"Parse error: {ex.Message}");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            return JsonRpcResponse.Failure(null, JsonRpcErrorCodes.InvalidRequest, "Invalid JSON-RPC request.");
        }

        // Notification: methods starting with notifications/ or requests with no id
        var isNotification = request.Id is null;

        try
        {
            switch (request.Method)
            {
                case "initialize":
                    var initResult = new InitializeResult(
                        ProtocolVersion: "2024-11-05",
                        Capabilities: new ServerCapabilities(Tools: new ToolsCapability(ListChanged: false)),
                        ServerInfo: new ServerInfo(Name: "cad-agent-mcp", Version: "0.1.0")
                    );
                    return JsonRpcResponse.Success(request.Id, initResult);

                case "notifications/initialized":
                    // Acknowledgment notification; do not send a response
                    return null;

                case "ping":
                    return JsonRpcResponse.Success(request.Id, new { });

                case "tools/list":
                    var tools = toolHandler.GetTools();
                    return JsonRpcResponse.Success(request.Id, new ToolsListResult(tools));

                case "tools/call":
                    if (!request.Params.HasValue || request.Params.Value.ValueKind != JsonValueKind.Object)
                    {
                        return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing parameters object.");
                    }

                    var paramsObj = request.Params.Value;
                    if (!paramsObj.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
                    {
                        return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing or non-string 'name' in tools/call parameters.");
                    }

                    var toolName = nameProp.GetString()!;
                    JsonElement? toolArguments = null;
                    if (paramsObj.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object)
                    {
                        toolArguments = argsProp;
                    }

                    var toolResult = await toolHandler.ExecuteToolAsync(toolName, toolArguments, cancellationToken).ConfigureAwait(false);
                    return JsonRpcResponse.Success(request.Id, toolResult);

                default:
                    if (isNotification)
                    {
                        return null;
                    }
                    return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'.");
            }
        }
        catch (Exception ex)
        {
            if (isNotification) return null;
            return JsonRpcResponse.Failure(request.Id, JsonRpcErrorCodes.InternalError, $"Internal error: {ex.Message}");
        }
    }
}
