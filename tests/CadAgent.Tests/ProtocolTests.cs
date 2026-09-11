using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CadAgent.Bridge;
using CadAgent.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CadAgent.Tests;

[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public async Task SerializationAndFramingRoundTrip()
    {
        var request = new RpcRequest(1, "r-1", "system.ping");
        await using var stream = new MemoryStream();
        await PipeFraming.WriteAsync(stream, request);
        stream.Position = 0;
        Assert.AreEqual(request, await PipeFraming.ReadAsync<RpcRequest>(stream));
        Assert.AreEqual("{\"version\":1,\"requestId\":\"r-1\",\"method\":\"system.ping\"}",
            Encoding.UTF8.GetString(stream.ToArray()[4..]));
    }

    [TestMethod]
    public async Task MalformedJsonIsRejected()
    {
        await using var stream = Framed("{");
        await Assert.ThrowsExceptionAsync<JsonException>(() => PipeFraming.ReadAsync<RpcRequest>(stream));
    }

    [TestMethod]
    public async Task OversizedMessageIsRejected()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, ProtocolConstants.MaximumMessageBytes + 1);
        await using var stream = new MemoryStream(header);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => PipeFraming.ReadAsync<RpcRequest>(stream));
    }

    [TestMethod]
    public void ContractContainsOnlyB1Allowlist()
    {
        CollectionAssert.AreEquivalent(new[]
        {
            "system.ping", "cad.get_drawing_info", "cad.list_layers", "cad.list_blocks"
        }, ProtocolConstants.AllowedMethods.ToArray());
        Assert.IsFalse(ProtocolConstants.AllowedMethods.Any(method =>
            method.Contains("create", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("execute", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void StructuredErrorSerializesPredictably()
    {
        var response = RpcResponse.Failure("r-2", "unknown_method", "Not allowed.");
        Assert.AreEqual(
            "{\"version\":1,\"requestId\":\"r-2\",\"ok\":false,\"error\":{\"code\":\"unknown_method\",\"message\":\"Not allowed.\"}}",
            JsonSerializer.Serialize(response, JsonDefaults.Options));
    }

    [TestMethod]
    public async Task ClientCorrelatesResponseAndReconnects()
    {
        var pipeName = $"cad-agent-test-{Guid.NewGuid():N}";
        var server = ServeAsync(pipeName, requests: 2, request => RpcResponse.Success(request.RequestId, new { pong = true }));
        var client = new PipeClient(pipeName, TimeSpan.FromSeconds(3));
        Assert.IsTrue((await client.CallAsync("system.ping")).Ok);
        Assert.IsTrue((await client.CallAsync("system.ping")).Ok);
        await server;
    }

    [TestMethod]
    public async Task ClientRejectsWrongCorrelationId()
    {
        var pipeName = $"cad-agent-test-{Guid.NewGuid():N}";
        var server = ServeAsync(pipeName, 1, request => RpcResponse.Success("wrong", new { request.RequestId }));
        var client = new PipeClient(pipeName, TimeSpan.FromSeconds(3));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.CallAsync("system.ping"));
        await server;
    }

    [TestMethod]
    public async Task ClientEnforcesTimeout()
    {
        var pipeName = $"cad-agent-test-{Guid.NewGuid():N}";
        var server = Task.Run(async () =>
        {
            await using var pipe = NewServer(pipeName);
            await pipe.WaitForConnectionAsync();
            await PipeFraming.ReadAsync<RpcRequest>(pipe);
            await Task.Delay(500);
        });
        var client = new PipeClient(pipeName, TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsExceptionAsync<TimeoutException>(() => client.CallAsync("system.ping"));
        await server;
    }

    private static MemoryStream Framed(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var bytes = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, body.Length);
        body.CopyTo(bytes, 4);
        return new MemoryStream(bytes);
    }

    private static async Task ServeAsync(string pipeName, int requests, Func<RpcRequest, RpcResponse> respond)
    {
        for (var i = 0; i < requests; i++)
        {
            await using var pipe = NewServer(pipeName);
            await pipe.WaitForConnectionAsync();
            var request = await PipeFraming.ReadAsync<RpcRequest>(pipe) ?? throw new InvalidDataException();
            await PipeFraming.WriteAsync(pipe, respond(request));
        }
    }

    private static NamedPipeServerStream NewServer(string name) => new(name, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
}
