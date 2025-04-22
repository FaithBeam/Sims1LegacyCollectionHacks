using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace Sims1LegacyHacks;

public class SimsHooked(Process simsProc, SafeFileHandle simsHandle)
{
    public nint BaseAddress { get; } = simsProc.MainModule!.BaseAddress;
    public SafeFileHandle SimsHandle { get; } = simsHandle;
    public Process SimsProcess { get; } = simsProc;
}

public partial class SimsProcess(ILogger<SimsProcess> logger, string simsPath, int sleepTime = 1000)
    : IDisposable
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

            _simsProc = new Process();
            _simsProc.StartInfo.FileName = _simsPath;
            _simsProc.StartInfo.WorkingDirectory = Path.GetDirectoryName(
                _simsProc.StartInfo.FileName
            );
            _simsProc.Start();
            _simsProc.WaitForInputIdle();

            if (_shouldStop)
            {
                return;
            }

            if (_simsProc is not null)
            {
                LogFoundSimsProcess(_logger, _simsProc);

                _simsProc.EnableRaisingEvents = true;
                _simsProc.Exited += SimsProcOnExited;

                LogAttemptToOpenSimsProcess(_logger, _simsProc.Id);
                var simsHandle = PInvoke.OpenProcess_SafeHandle(
                    PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS,
                    false,
                    (uint)_simsProc.Id
                );
                if (simsHandle.IsInvalid)
                {
                    throw new Exception($"Error opening process pid: {_simsProc.Id}");
                }

                LogHandleSuccess(_logger);
                LogStopMonitoringForSimsProcess(_logger);
                _simsHookedSubject.OnNext(new SimsHooked(_simsProc, simsHandle));
            }
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
        _simsProc = null;
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

    private Process? _simsProc;
    private Thread? _simsThread;
    private readonly ILogger _logger = logger;
    private readonly string _simsPath = simsPath;
    private readonly int _sleepTime = sleepTime;
    private bool _shouldStop = false;
    private readonly Subject<bool> _hookEnabledSubject = new();
    private readonly Subject<bool> _hookDisabledSubject = new();
    private readonly Subject<bool> _simsProcExitedSubject = new();
    private readonly Subject<SimsHooked> _simsHookedSubject = new();
}
