using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadAgent.Mcp;

public static class McpJsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params
);

public sealed record JsonRpcResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] JsonRpcError? Error = null
)
{
    public static JsonRpcResponse Success(JsonElement? id, object? result) =>
        new("2.0", id, Result: result);

    public static JsonRpcResponse Failure(JsonElement? id, int code, string message, object? data = null) =>
        new("2.0", id, Error: new JsonRpcError(code, message, data));
}

public sealed record JsonRpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] object? Data = null
);

public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

public sealed record InitializeResult(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("capabilities")] ServerCapabilities Capabilities,
    [property: JsonPropertyName("serverInfo")] ServerInfo ServerInfo
);

public sealed record ServerCapabilities(
    [property: JsonPropertyName("tools")] ToolsCapability? Tools = null
);

public sealed record ToolsCapability(
    [property: JsonPropertyName("listChanged")] bool? ListChanged = null
);

public sealed record ServerInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version
);

public sealed record ToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] JsonElement InputSchema
);

public sealed record ToolsListResult(
    [property: JsonPropertyName("tools")] IReadOnlyList<ToolDefinition> Tools
);

public sealed record ToolCallResult(
    [property: JsonPropertyName("content")] IReadOnlyList<ContentItem> Content,
    [property: JsonPropertyName("isError")] bool IsError = false
)
{
    public static ToolCallResult Success(string text) =>
        new([new ContentItem("text", text)], IsError: false);

    public static ToolCallResult Error(string text) =>
        new([new ContentItem("text", text)], IsError: true);
}

public sealed record ContentItem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text
);
