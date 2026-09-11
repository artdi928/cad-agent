using CadAgent.Protocol;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadAgent.Plugin;

internal static class PluginRuntime
{
    private const int QueueCapacity = 32;
    private const int MaximumRequestsPerIdle = 4;
    private static readonly object Sync = new();
    private static readonly CadDispatcher Dispatcher = new();
    private static CadRequestPump? _pump;
    private static PipeServer? _server;
    private static DateTimeOffset? _startedAt;
    private static string _state = "not_initialized";
    private static string? _lastError;

    public static string Status
    {
        get
        {
            lock (Sync)
            {
                var uptime = _startedAt is null ? "n/a" : (DateTimeOffset.UtcNow - _startedAt.Value).ToString("hh\\:mm\\:ss");
                return $"CadAgent B1 state={_state}; pipe={_server?.PipeName ?? "offline"}; " +
                       $"uptime={uptime}; pending={_pump?.PendingCount ?? 0}; lastError={_lastError ?? "none"}";
            }
        }
    }

    public static void Start()
    {
        lock (Sync)
        {
            if (_state == "ready") return;
            try
            {
                _state = "starting";
                _lastError = null;
                _startedAt = DateTimeOffset.UtcNow;
                _pump = new CadRequestPump(QueueCapacity);
                AcApplication.Idle += OnIdle;
                _server = new PipeServer(
                    Environment.GetEnvironmentVariable("CAD_AGENT_PIPE") ?? ProtocolConstants.DefaultPipeName,
                    _pump);
                _server.Start();
                _state = "ready";
                Trace("plugin initialize");
            }
            catch (Exception exception)
            {
                _state = "faulted";
                _lastError = exception.Message;
                try { AcApplication.Idle -= OnIdle; } catch { }
                _pump?.Stop();
                _server?.Dispose();
                _server = null;
                Trace($"plugin initialize failed: {exception}");
            }
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            if (_state is "stopped" or "not_initialized") return;
            _state = "stopping";
            try { AcApplication.Idle -= OnIdle; } catch (Exception exception) { RecordError(exception); }
            _pump?.Stop();
            _server?.Dispose();
            _server = null;
            _state = "stopped";
            Trace("plugin terminate");
        }
    }

    public static void RecordError(Exception exception)
    {
        lock (Sync) _lastError = exception.Message;
        Trace(exception.ToString());
    }

    private static void OnIdle(object? sender, EventArgs args)
    {
        try { _pump?.Drain(MaximumRequestsPerIdle, Dispatcher.Dispatch); }
        catch (Exception exception) { RecordError(exception); }
    }

    private static void Trace(string message) =>
        System.Diagnostics.Trace.WriteLine($"CadAgent {DateTimeOffset.UtcNow:O} {message}");
}
