using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CadAgent.Plugin.PluginEntry))]
[assembly: CommandClass(typeof(CadAgent.Plugin.PluginEntry))]

namespace CadAgent.Plugin;

public sealed class PluginEntry : IExtensionApplication
{
    private PipeServer? _server;
    private DateTimeOffset _startedAt;
    private string? _lastError;

    public void Initialize()
    {
        try
        {
            _startedAt = DateTimeOffset.UtcNow;
            _server = new PipeServer(
                Environment.GetEnvironmentVariable("CAD_AGENT_PIPE") ?? CadAgent.Protocol.ProtocolConstants.DefaultPipeName,
                new CadDispatcher());
            _server.Start();
            Write("CadAgent B1 ready. Commands: ECA_PING, ECA_STATUS.");
        }
        catch (System.Exception exception)
        {
            _lastError = exception.Message;
            _server?.Dispose();
            _server = null;
            Write($"CadAgent initialization failed: {exception.Message}");
        }
    }

    public void Terminate()
    {
        try { _server?.Dispose(); }
        catch (System.Exception exception) { _lastError = exception.Message; }
    }

    [CommandMethod("ECA_PING")]
    public void Ping() => Write("CadAgent pong");

    [CommandMethod("ECA_STATUS")]
    public void Status() => Write(
        $"CadAgent B1 pipe={_server?.PipeName ?? "offline"}; uptime={DateTimeOffset.UtcNow - _startedAt:g}; lastError={_lastError ?? "none"}");

    private static void Write(string message)
    {
        try { AcApplication.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n{message}\n"); }
        catch { }
    }
}
