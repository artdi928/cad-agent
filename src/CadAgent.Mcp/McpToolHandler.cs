using System.Text.Json;
using CadAgent.Bridge;
using CadAgent.Protocol;

namespace CadAgent.Mcp;

public interface ICadBridgeClient
{
    Task<RpcResponse> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default);
}

public sealed class PipeBridgeClient(string pipeName = ProtocolConstants.DefaultPipeName, TimeSpan? timeout = null) : ICadBridgeClient
{
    private readonly PipeClient _client = new(pipeName, timeout);

    public Task<RpcResponse> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default) =>
        _client.CallAsync(method, parameters, cancellationToken);
}

public sealed class McpToolHandler(ICadBridgeClient bridgeClient)
{
    private static readonly IReadOnlyList<ToolDefinition> Tools =
    [
        new(
            "cad_ping",
            "Ping the AutoCAD CadAgent plugin and verify live connectivity, process ID, and protocol version.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """).RootElement
        ),
        new(
            "cad_get_drawing_info",
            "Get authoritative drawing information of the active AutoCAD document including file path, current layer, measurement system, and insertion units.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """).RootElement
        ),
        new(
            "cad_list_layers",
            "List all layers in the active AutoCAD drawing along with their visibility, freeze, lock, and color properties.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """).RootElement
        ),
        new(
            "cad_list_blocks",
            "List all block definition names in the active AutoCAD drawing block table.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """).RootElement
        ),
        new(
            "cad_list_entities",
            "Inspect and list entities in ModelSpace and PaperSpace. Supported types: DBText, BlockReference, MText, MLeader. Optionally filter by entity types.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "types": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Optional filter of entity type names, e.g. ['DBText', 'BlockReference']."
                }
              },
              "additionalProperties": false
            }
            """).RootElement
        ),
        new(
            "cad_validate_change_plan",
            "Perform dry-run preflight validation of a structured change plan against the active drawing. Validates active drawing identity, entity handles, object types, and precondition values ForRead before any write is attempted.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "plan": {
                  "type": "object",
                  "description": "The structured change plan object.",
                  "properties": {
                    "version": { "type": "integer", "enum": [1] },
                    "drawing": {
                      "type": "object",
                      "properties": {
                        "fullPath": { "type": "string", "description": "Exact disk path of target drawing." }
                      },
                      "required": ["fullPath"]
                    },
                    "operations": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "operationId": { "type": "string" },
                          "kind": { "type": "string", "enum": ["set_dbtext", "set_block_attribute"] },
                          "target": {
                            "type": "object",
                            "properties": {
                              "handle": { "type": "string" },
                              "attributeTag": { "type": "string" }
                            },
                            "required": ["handle"]
                          },
                          "precondition": {
                            "type": "object",
                            "properties": {
                              "equals": { "type": "string" }
                            },
                            "required": ["equals"]
                          },
                          "value": { "type": "string" }
                        },
                        "required": ["operationId", "kind", "target", "precondition", "value"]
                      }
                    }
                  },
                  "required": ["version", "drawing", "operations"]
                }
              },
              "required": ["plan"],
              "additionalProperties": false
            }
            """).RootElement
        ),
        new(
            "cad_apply_change_plan",
            "Atomically apply a validated change plan to the active drawing. Enforces pre-write preflight validation, single transaction mutation, post-write actual value postcondition verification, and automatic rollback with authoritative re-read verification on failure.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "plan": {
                  "type": "object",
                  "description": "The structured change plan object to apply.",
                  "properties": {
                    "version": { "type": "integer", "enum": [1] },
                    "drawing": {
                      "type": "object",
                      "properties": {
                        "fullPath": { "type": "string", "description": "Exact disk path of target drawing." }
                      },
                      "required": ["fullPath"]
                    },
                    "operations": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "operationId": { "type": "string" },
                          "kind": { "type": "string", "enum": ["set_dbtext", "set_block_attribute"] },
                          "target": {
                            "type": "object",
                            "properties": {
                              "handle": { "type": "string" },
                              "attributeTag": { "type": "string" }
                            },
                            "required": ["handle"]
                          },
                          "precondition": {
                            "type": "object",
                            "properties": {
                              "equals": { "type": "string" }
                            },
                            "required": ["equals"]
                          },
                          "value": { "type": "string" }
                        },
                        "required": ["operationId", "kind", "target", "precondition", "value"]
                      }
                    }
                  },
                  "required": ["version", "drawing", "operations"]
                }
              },
              "required": ["plan"],
              "additionalProperties": false
            }
            """).RootElement
        )
    ];

    public IReadOnlyList<ToolDefinition> GetTools() => Tools;

    public async Task<ToolCallResult> ExecuteToolAsync(string toolName, JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            return toolName switch
            {
                "cad_ping" => await ExecutePingAsync(cancellationToken),
                "cad_get_drawing_info" => await ExecuteRpcAsync("cad.get_drawing_info", null, cancellationToken),
                "cad_list_layers" => await ExecuteRpcAsync("cad.list_layers", null, cancellationToken),
                "cad_list_blocks" => await ExecuteRpcAsync("cad.list_blocks", null, cancellationToken),
                "cad_list_entities" => await ExecuteListEntitiesAsync(arguments, cancellationToken),
                "cad_validate_change_plan" => await ExecutePlanAsync("cad.validate_change_plan", arguments, cancellationToken),
                "cad_apply_change_plan" => await ExecutePlanAsync("cad.apply_change_plan", arguments, cancellationToken),
                _ => ToolCallResult.Error(FormatJson(new
                {
                    ok = false,
                    error = new
                    {
                        code = "METHOD_NOT_FOUND",
                        message = $"Unknown tool '{toolName}'."
                    }
                }))
            };
        }
        catch (TimeoutException ex)
        {
            return ToolCallResult.Error(FormatJson(new
            {
                ok = false,
                error = new
                {
                    code = "BRIDGE_TIMEOUT",
                    message = ex.Message
                }
            }));
        }
        catch (Exception ex)
        {
            return ToolCallResult.Error(FormatJson(new
            {
                ok = false,
                error = new
                {
                    code = "BRIDGE_UNAVAILABLE",
                    message = ex.Message
                }
            }));
        }
    }

    private async Task<ToolCallResult> ExecutePingAsync(CancellationToken cancellationToken)
    {
        var response = await bridgeClient.CallAsync("system.ping", null, cancellationToken);
        return FormatRpcResponse(response);
    }

    private async Task<ToolCallResult> ExecuteRpcAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        var response = await bridgeClient.CallAsync(method, parameters, cancellationToken);
        return FormatRpcResponse(response);
    }

    private async Task<ToolCallResult> ExecuteListEntitiesAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        JsonElement? parameters = null;
        if (arguments.HasValue && arguments.Value.ValueKind == JsonValueKind.Object)
        {
            if (arguments.Value.TryGetProperty("types", out var typesProp) && typesProp.ValueKind == JsonValueKind.Array)
            {
                parameters = JsonSerializer.SerializeToElement(new { types = typesProp }, JsonDefaults.Options);
            }
        }

        var response = await bridgeClient.CallAsync("cad.list_entities", parameters, cancellationToken);
        return FormatRpcResponse(response);
    }

    private async Task<ToolCallResult> ExecutePlanAsync(string method, JsonElement? arguments, CancellationToken cancellationToken)
    {
        if (!arguments.HasValue || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return ToolCallResult.Error(FormatJson(new
            {
                ok = false,
                error = new
                {
                    code = "INVALID_CHANGE_PLAN",
                    message = "Missing arguments object. Expected 'plan' parameter."
                }
            }));
        }

        if (!arguments.Value.TryGetProperty("plan", out var planElement) || planElement.ValueKind != JsonValueKind.Object)
        {
            return ToolCallResult.Error(FormatJson(new
            {
                ok = false,
                error = new
                {
                    code = "INVALID_CHANGE_PLAN",
                    message = "Missing or malformed 'plan' property in arguments."
                }
            }));
        }

        var parameters = JsonSerializer.SerializeToElement(new { plan = planElement }, JsonDefaults.Options);
        var response = await bridgeClient.CallAsync(method, parameters, cancellationToken);
        return FormatRpcResponse(response);
    }

    private static ToolCallResult FormatRpcResponse(RpcResponse response)
    {
        var json = FormatJson(response);
        return response.Ok
            ? ToolCallResult.Success(json)
            : ToolCallResult.Error(json);
    }

    private static string FormatJson(object value) =>
        JsonSerializer.Serialize(value, JsonDefaults.Options);
}
