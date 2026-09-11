using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CadAgent.Plugin.PluginEntry))]
[assembly: CommandClass(typeof(CadAgent.Plugin.PluginEntry))]

namespace CadAgent.Plugin;

public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize() => PluginRuntime.Start();
    public void Terminate() => PluginRuntime.Stop();

    [CommandMethod("ECA_PING")]
    public void Ping() => Write("CadAgent pong");

    [CommandMethod("ECA_STATUS")]
    public void Status() => Write(PluginRuntime.Status);

    private static void Write(string message)
    {
        try { AcApplication.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n{message}\n"); }
        catch { }
    }
}
