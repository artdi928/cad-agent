using CadAgent.Mcp;
using CadAgent.Protocol;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var pipeName = Environment.GetEnvironmentVariable("CAD_AGENT_PIPE") ?? ProtocolConstants.DefaultPipeName;
    var bridgeClient = new PipeBridgeClient(pipeName);
    var toolHandler = new McpToolHandler(bridgeClient);
    var server = new McpServer(toolHandler);

    await server.RunAsync(Console.In, Console.Out, cts.Token);
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[CadAgent.Mcp Fatal] {ex}");
    return 1;
}
