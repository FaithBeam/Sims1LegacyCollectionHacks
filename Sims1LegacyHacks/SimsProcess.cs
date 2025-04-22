using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace Sims1LegacyHacks;

public class SimsHooked(Process simsProcess, SafeFileHandle simsHandle)
{
    public nint BaseAddress { get; } = simsProcess.MainModule!.BaseAddress;
    public SafeFileHandle SimsHandle { get; } = simsHandle;
    public Process SimsProcess { get; } = simsProcess;
}

public class SimsProcessSettings
{
    public string? SimsPath { get; set; }
}

public partial class SimsProcess(
    ILogger<SimsProcess> logger,
    SimsProcessSettings settings,
    int sleepTime = 1000
) : IDisposable
{
    /// <summary>
    /// Searching for Sims.exe event
    /// </summary>
    public IObservable<bool> HookEnabled => _hookEnabledSubject.AsObservable();
    public IObservable<bool> HookDisabled => _hookDisabledSubject.AsObservable();

    /// <summary>
    ///
    /// </summary>
    public IObservable<SimsHooked> SimsHooked => _simsHookedSubject.AsObservable();

    /// <summary>
    /// Sims.exe has exited event
    /// </summary>
    public IObservable<bool> SimsProcExited => _simsProcExitedSubject.AsObservable();

    [SupportedOSPlatform("windows5.1.2600")]
    public void Start()
    {
        LogStartup(_logger);
        _simsThread = new Thread(() =>
        {
            _hookEnabledSubject.OnNext(true);
            // do
            // {
            //     _simsProc = Process.GetProcessesByName("Sims").SingleOrDefault();
            //     Thread.Sleep(_sleepTime);
            // } while (_simsProc is null && !_shouldStop);

            var simsDir = Path.GetDirectoryName(_settings.SimsPath);
            var simsSteamAppIdPath = Path.Combine(simsDir!, "steam_appid.txt");
            if (!File.Exists(simsSteamAppIdPath))
            {
                using var steamAppIdFile = new StreamWriter(simsSteamAppIdPath);
                steamAppIdFile.WriteLine("3314060");
            }
            var simsProc = new Process();
            simsProc.StartInfo.FileName = _settings.SimsPath;
            simsProc.StartInfo.WorkingDirectory = simsDir;
            simsProc.Start();
            simsProc.WaitForInputIdle();

            if (_shouldStop)
            {
                return;
            }

            LogFoundSimsProcess(_logger, simsProc);

            simsProc.EnableRaisingEvents = true;
            simsProc.Exited += SimsProcOnExited;

            LogAttemptToOpenSimsProcess(_logger, simsProc.Id);
            var simsHandle = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS,
                false,
                (uint)simsProc.Id
            );
            if (simsHandle.IsInvalid)
            {
                throw new Exception($"Error opening process pid: {simsProc.Id}");
            }

            LogHandleSuccess(_logger);
            LogStopMonitoringForSimsProcess(_logger);
            _simsHookedSubject.OnNext(new SimsHooked(simsProc, simsHandle));
        });
        _simsThread.Start();
    }

    private void SimsProcOnExited(object? sender, EventArgs e)
    {
        LogSimsExited(_logger);
        _simsProcExitedSubject.OnNext(true);
        Stop();
    }

    private void Stop()
    {
        _shouldStop = true;
        _simsThread?.Join();
        _hookDisabledSubject.OnNext(true);
        // _simsProc = null;
    }

    public void Dispose() { }

    [LoggerMessage(LogLevel.Information, "Monitoring processes for Sims.exe")]
    public static partial void LogStartup(ILogger l);

    [LoggerMessage(LogLevel.Information, "Found Sims process: {Process}")]
    public static partial void LogFoundSimsProcess(ILogger l, Process process);

    [LoggerMessage(LogLevel.Information, "Attempting to retrieve a handle to {Pid}")]
    public static partial void LogAttemptToOpenSimsProcess(ILogger l, int pid);

    [LoggerMessage(LogLevel.Information, "Retrieving a handle successful")]
    public static partial void LogHandleSuccess(ILogger l);

    [LoggerMessage(LogLevel.Information, "Stop monitoring processes for Sims.exe")]
    public static partial void LogStopMonitoringForSimsProcess(ILogger l);

    [LoggerMessage(LogLevel.Information, "Sims.exe has exited")]
    public static partial void LogSimsExited(ILogger l);

    private Thread? _simsThread;
    private readonly ILogger _logger = logger;
    private readonly SimsProcessSettings _settings = settings;
    private readonly int _sleepTime = sleepTime;
    private bool _shouldStop = false;
    private readonly Subject<bool> _hookEnabledSubject = new();
    private readonly Subject<bool> _hookDisabledSubject = new();
    private readonly Subject<bool> _simsProcExitedSubject = new();
    private readonly Subject<SimsHooked> _simsHookedSubject = new();
}
