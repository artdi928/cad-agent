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
    public void ContractContainsOnlyAllowlistedMethods()
    {
        CollectionAssert.AreEquivalent(new[]
        {
            "system.ping",
            "cad.get_drawing_info",
            "cad.list_layers",
            "cad.list_blocks",
            "cad.list_entities",
            "cad.validate_change_plan",
            "cad.apply_change_plan"
        }, ProtocolConstants.AllowedMethods.ToArray());
        Assert.IsFalse(ProtocolConstants.AllowedMethods.Any(method =>
            method.Contains("create", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("execute", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("lisp", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("save", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("shell", StringComparison.OrdinalIgnoreCase)));
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
    public async Task RequestPumpEnqueuesAndCompletesCorrelatedRequest()
    {
        var pump = new CadRequestPump();
        var request = new RpcRequest(1, "queued-1", "system.ping");
        var completion = pump.Enqueue(request, CancellationToken.None);
        Assert.AreEqual(1, pump.PendingCount);
        Assert.AreEqual(1, pump.Drain(1, item => RpcResponse.Success(item.RequestId, new { pong = true })));
        var response = await completion;
        Assert.IsTrue(response.Ok);
        Assert.AreEqual(request.RequestId, response.RequestId);
        Assert.AreEqual(0, pump.PendingCount);
    }

    [TestMethod]
    public async Task RequestPumpRejectsOverflow()
    {
        var pump = new CadRequestPump(capacity: 1);
        _ = pump.Enqueue(new RpcRequest(1, "first", "system.ping"), CancellationToken.None);
        var rejected = await pump.Enqueue(new RpcRequest(1, "second", "system.ping"), CancellationToken.None);
        Assert.IsFalse(rejected.Ok);
        Assert.AreEqual("server_busy", rejected.Error?.Code);
    }

    [TestMethod]
    public async Task RequestPumpCancellationIsStructured()
    {
        var pump = new CadRequestPump();
        using var cancellation = new CancellationTokenSource();
        var completion = pump.Enqueue(new RpcRequest(1, "cancelled", "system.ping"), cancellation.Token);
        cancellation.Cancel();
        Assert.AreEqual("request_timeout", (await completion).Error?.Code);
        pump.Drain(1, _ => throw new AssertFailedException("Cancelled request must not dispatch."));
    }

    [TestMethod]
    public async Task RequestPumpShutdownFailsPendingAndNewRequests()
    {
        var pump = new CadRequestPump();
        var pending = pump.Enqueue(new RpcRequest(1, "pending", "system.ping"), CancellationToken.None);
        pump.Stop();
        Assert.AreEqual("plugin_stopping", (await pending).Error?.Code);
        var afterStop = await pump.Enqueue(new RpcRequest(1, "late", "system.ping"), CancellationToken.None);
        Assert.AreEqual("plugin_stopping", afterStop.Error?.Code);
        Assert.AreEqual(0, pump.PendingCount);
    }

    [TestMethod]
    public void ProtocolValidationRejectsUnknownMethodAndVersion()
    {
        Assert.AreEqual("unknown_method",
            ProtocolValidation.Validate(new RpcRequest(1, "r", "cad.create"))?.Error?.Code);
        Assert.AreEqual("protocol_mismatch",
            ProtocolValidation.Validate(new RpcRequest(2, "r", "system.ping"))?.Error?.Code);
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
