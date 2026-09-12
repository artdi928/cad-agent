using System.Text.Json;
using CadAgent.Mcp;
using CadAgent.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CadAgent.Tests;

[TestClass]
public sealed class McpTests
{
    private sealed class MockBridgeClient : ICadBridgeClient
    {
        public string? LastMethod { get; private set; }
        public JsonElement? LastParameters { get; private set; }
        public Func<string, JsonElement?, RpcResponse>? ResponseFactory { get; set; }

        public Task<RpcResponse> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            LastMethod = method;
            LastParameters = parameters;

            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(method, parameters));
            }

            return Task.FromResult(RpcResponse.Success("test-req-id", new { ok = true }));
        }
    }

    [TestMethod]
    public void McpToolHandler_GetTools_ExposesExactlySevenTools()
    {
        var mock = new MockBridgeClient();
        var handler = new McpToolHandler(mock);
        var tools = handler.GetTools();

        Assert.AreEqual(7, tools.Count);
        var toolNames = tools.Select(t => t.Name).ToHashSet();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "cad_ping",
                "cad_get_drawing_info",
                "cad_list_layers",
                "cad_list_blocks",
                "cad_list_entities",
                "cad_validate_change_plan",
                "cad_apply_change_plan"
            },
            tools.Select(t => t.Name).ToList());

        foreach (var tool in tools)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(tool.Description), $"Tool '{tool.Name}' must have a description.");
            Assert.AreEqual(JsonValueKind.Object, tool.InputSchema.ValueKind, $"Tool '{tool.Name}' must have an object schema.");
        }
    }

    [TestMethod]
    public async Task McpServer_Initialize_ReturnsExpectedProtocolAndCapabilities()
    {
        var mock = new MockBridgeClient();
        var handler = new McpToolHandler(mock);
        var server = new McpServer(handler);

        var requestJson = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""";
        var response = await server.ProcessMessageAsync(requestJson);

        Assert.IsNotNull(response);
        Assert.IsNull(response.Error);
        Assert.AreEqual(1, response.Id?.GetInt32());

        var initResult = response.Result as InitializeResult;
        Assert.IsNotNull(initResult);
        Assert.AreEqual("2024-11-05", initResult.ProtocolVersion);
        Assert.AreEqual("cad-agent-mcp", initResult.ServerInfo.Name);
        Assert.IsNotNull(initResult.Capabilities.Tools);
    }

    [TestMethod]
    public async Task McpServer_Ping_ReturnsEmptyObject()
    {
        var mock = new MockBridgeClient();
        var handler = new McpToolHandler(mock);
        var server = new McpServer(handler);

        var requestJson = """{"jsonrpc":"2.0","id":2,"method":"ping"}""";
        var response = await server.ProcessMessageAsync(requestJson);

        Assert.IsNotNull(response);
        Assert.IsNull(response.Error);
        Assert.AreEqual(2, response.Id?.GetInt32());
    }

    [TestMethod]
    public async Task McpServer_UnknownMethod_ReturnsMethodNotFound()
    {
        var mock = new MockBridgeClient();
        var handler = new McpToolHandler(mock);
        var server = new McpServer(handler);

        var requestJson = """{"jsonrpc":"2.0","id":3,"method":"unknown_method"}""";
        var response = await server.ProcessMessageAsync(requestJson);

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(JsonRpcErrorCodes.MethodNotFound, response.Error.Code);
    }

    [TestMethod]
    public async Task McpServer_ToolsList_ReturnsAllToolDefinitions()
    {
        var mock = new MockBridgeClient();
        var handler = new McpToolHandler(mock);
        var server = new McpServer(handler);

        var requestJson = """{"jsonrpc":"2.0","id":4,"method":"tools/list"}""";
        var response = await server.ProcessMessageAsync(requestJson);

        Assert.IsNotNull(response);
        Assert.IsNull(response.Error);
        var listResult = response.Result as ToolsListResult;
        Assert.IsNotNull(listResult);
        Assert.AreEqual(7, listResult.Tools.Count);
    }

    [TestMethod]
    public async Task McpToolHandler_CadPing_DelegatesToBridge()
    {
        var mock = new MockBridgeClient
        {
            ResponseFactory = (method, _) =>
            {
                Assert.AreEqual("system.ping", method);
                return RpcResponse.Success("req-1", new { pong = true, processId = 12345 });
            }
        };

        var handler = new McpToolHandler(mock);
        var result = await handler.ExecuteToolAsync("cad_ping", null);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(1, result.Content.Count);
        Assert.IsTrue(result.Content[0].Text.Contains("12345"));
        Assert.IsTrue(result.Content[0].Text.Contains("pong"));
    }

    [TestMethod]
    public async Task McpToolHandler_CadGetDrawingInfo_DelegatesToBridge()
    {
        var mock = new MockBridgeClient
        {
            ResponseFactory = (method, _) =>
            {
                Assert.AreEqual("cad.get_drawing_info", method);
                return RpcResponse.Success("req-2", new { name = @"C:\Temp\test.dwg", currentLayer = "0" });
            }
        };

        var handler = new McpToolHandler(mock);
        var result = await handler.ExecuteToolAsync("cad_get_drawing_info", null);

        Assert.IsFalse(result.IsError);
        Assert.IsTrue(result.Content[0].Text.Contains("test.dwg"));
    }

    [TestMethod]
    public async Task McpToolHandler_CadListEntities_PassesTypesFilter()
    {
        var mock = new MockBridgeClient
        {
            ResponseFactory = (method, parameters) =>
            {
                Assert.AreEqual("cad.list_entities", method);
                Assert.IsNotNull(parameters);
                Assert.IsTrue(parameters.Value.TryGetProperty("types", out var types));
                Assert.AreEqual("DBText", types[0].GetString());
                return RpcResponse.Success("req-3", new[] { new { handle = "305", entityType = "DBText", text = "OLD_A" } });
            }
        };

        var handler = new McpToolHandler(mock);
        var args = JsonDocument.Parse("""{"types": ["DBText"]}""").RootElement;
        var result = await handler.ExecuteToolAsync("cad_list_entities", args);

        Assert.IsFalse(result.IsError);
        Assert.IsTrue(result.Content[0].Text.Contains("305"));
        Assert.IsTrue(result.Content[0].Text.Contains("OLD_A"));
    }

    [TestMethod]
    public async Task McpToolHandler_CadValidateChangePlan_ForwardsPlanParameter()
    {
        var mock = new MockBridgeClient
        {
            ResponseFactory = (method, parameters) =>
            {
                Assert.AreEqual("cad.validate_change_plan", method);
                Assert.IsNotNull(parameters);
                Assert.IsTrue(parameters.Value.TryGetProperty("plan", out var plan));
                Assert.AreEqual(1, plan.GetProperty("version").GetInt32());
                return RpcResponse.Success("req-4", new { valid = true, operations = new[] { new { status = "VALID" } } });
            }
        };

        var handler = new McpToolHandler(mock);
        var args = JsonDocument.Parse("""
        {
          "plan": {
            "version": 1,
            "drawing": { "fullPath": "C:\\Temp\\test.dwg" },
            "operations": [
              {
                "operationId": "op-1",
                "kind": "set_dbtext",
                "target": { "handle": "305" },
                "precondition": { "equals": "OLD_A" },
                "value": "NEW_A"
              }
            ]
          }
        }
        """).RootElement;

        var result = await handler.ExecuteToolAsync("cad_validate_change_plan", args);
        Assert.IsFalse(result.IsError);
        Assert.IsTrue(result.Content[0].Text.Contains("VALID"));
    }

    [TestMethod]
    public async Task McpToolHandler_CadApplyChangePlan_ForwardsPlanParameter()
    {
        var mock = new MockBridgeClient
        {
            ResponseFactory = (method, parameters) =>
            {
                Assert.AreEqual("cad.apply_change_plan", method);
                Assert.IsNotNull(parameters);
                Assert.IsTrue(parameters.Value.TryGetProperty("plan", out var plan));
                return RpcResponse.Success("req-5", new
                {
                    applied = true,
                    postconditionVerifiedBeforeCommit = true,
                    rollbackVerified = (bool?)null,
                    operations = new[] { new { operationId = "op-1", status = "APPLIED" } }
                });
            }
        };

        var handler = new McpToolHandler(mock);
        var args = JsonDocument.Parse("""
        {
          "plan": {
            "version": 1,
            "drawing": { "fullPath": "C:\\Temp\\test.dwg" },
            "operations": [
              {
                "operationId": "op-1",
                "kind": "set_dbtext",
                "target": { "handle": "305" },
                "precondition": { "equals": "OLD_A" },
                "value": "NEW_A"
              }
            ]
          }
        }
        """).RootElement;

        var result = await handler.ExecuteToolAsync("cad_apply_change_plan", args);
        Assert.IsFalse(result.IsError);
        Assert.IsTrue(result.Content[0].Text.Contains("APPLIED"));
    }

    [TestMethod]
    public async Task McpToolHandler_MissingPlanParameter_FailsClosed()
    {
        var mock = new MockBridgeClient();
        var handler = new McpToolHandler(mock);

        var result = await handler.ExecuteToolAsync("cad_validate_change_plan", null);
        Assert.IsTrue(result.IsError);
        Assert.IsTrue(result.Content[0].Text.Contains("INVALID_CHANGE_PLAN"));
        Assert.IsNull(mock.LastMethod, "Bridge must not be invoked when arguments are missing.");
    }

    [TestMethod]
    public async Task McpToolHandler_BridgeFailure_PreservesErrorCodeAndDetails()
    {
        var mock = new MockBridgeClient
        {
            ResponseFactory = (_, _) => RpcResponse.Failure(
                "req-err",
                ErrorCodes.PreconditionFailed,
                "Precondition mismatch for DBText '305'",
                new { valid = false })
        };

        var handler = new McpToolHandler(mock);
        var args = JsonDocument.Parse("""{"plan": {"version": 1}}""").RootElement;
        var result = await handler.ExecuteToolAsync("cad_validate_change_plan", args);

        Assert.IsTrue(result.IsError);
        Assert.IsTrue(result.Content[0].Text.Contains("PRECONDITION_FAILED"));
        Assert.IsTrue(result.Content[0].Text.Contains("Precondition mismatch for DBText '305'"));
    }

    [TestMethod]
    public async Task McpServer_StdioFullSession_RunsEndToEnd()
    {
        var mock = new MockBridgeClient
        {
            ResponseFactory = (m, _) => m == "system.ping"
                ? RpcResponse.Success("r1", new { pong = true, processId = 4444 })
                : RpcResponse.Success("r2", new { ok = true })
        };

        var handler = new McpToolHandler(mock);
        var server = new McpServer(handler);

        var input = string.Join("\n",
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"cad_ping","arguments":{}}}""",
            ""
        );

        using var reader = new StringReader(input);
        using var writer = new StringWriter();

        await server.RunAsync(reader, writer);

        var outputLines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(3, outputLines.Length, "Expected 3 responses (initialize, tools/list, tools/call; notification has no response).");

        Assert.IsTrue(outputLines[0].Contains("cad-agent-mcp"));
        Assert.IsTrue(outputLines[1].Contains("cad_validate_change_plan"));
        Assert.IsTrue(outputLines[2].Contains("4444"));
    }
}
