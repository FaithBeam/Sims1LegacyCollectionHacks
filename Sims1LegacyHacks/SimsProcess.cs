using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
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
    public SimsInstallType? SimsInstallType { get; set; }
    public bool AutoStart { get; set; }
    public int MonitorInterval { get; set; } = 1000;
}

[SupportedOSPlatform("windows5.1.2600")]
public partial class SimsProcess(ILogger<SimsProcess> logger, SimsProcessSettings settings)
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

    public void Start()
    {
        LogStartup(_logger);
        _simsThread = new Thread(() =>
        {
            _hookEnabledSubject.OnNext(true);
            Process? simsProc;

            // autostart is enabled, so we should start sims.exe
            if (_settings.AutoStart)
            {
                // eagerly assume the user has provided the path to the sims.exe
                var simsPath = _settings.SimsPath;
                var simsDir = Path.GetDirectoryName(simsPath);
                var simsInstallType = _settings.SimsInstallType;

                // if the user didn't provide the path to the sims.exe, we need to find it
                if (string.IsNullOrWhiteSpace(_settings.SimsPath))
                {
                    var simsInstall =
                        GetSimsPath() ?? throw new Exception("Unable to find Sims.exe");
                    simsPath = simsInstall.simsPath;
                    simsDir = Path.GetDirectoryName(simsPath);
                    simsInstallType = simsInstall.simsInstallType;
                }

                switch (simsInstallType)
                {
                    case SimsInstallType.Steam:
                        var simsSteamAppIdPath = Path.Combine(simsDir!, "steam_appid.txt");
                        if (!File.Exists(simsSteamAppIdPath))
                        {
                            using var steamAppIdFile = new StreamWriter(simsSteamAppIdPath);
                            steamAppIdFile.WriteLine("3314060");
                        }
                        break;
                    case SimsInstallType.Ea:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                simsProc = new Process();
                simsProc.StartInfo.FileName = simsPath;
                simsProc.StartInfo.WorkingDirectory = simsDir;
                simsProc.Start();
                simsProc.WaitForInputIdle();
            }
            else
            {
                // autostart is disabled, so we should wait for sims.exe to start
                do
                {
                    simsProc = Process.GetProcessesByName("Sims").SingleOrDefault();
                    Thread.Sleep(_settings.MonitorInterval);
                } while (simsProc is null && !_shouldStop);
            }

            if (simsProc is null)
            {
                throw new Exception("Unable to find Sims.exe process");
            }

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

    private (string simsPath, SimsInstallType simsInstallType)? GetSimsPath()
    {
        var steamPathValue =
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null)
            as string;
        if (!string.IsNullOrWhiteSpace(steamPathValue))
        {
            var simsExePath = Path.Combine(
                steamPathValue,
                @"steamapps\common\The Sims Legacy Collection\Sims.exe"
            );
            if (File.Exists(simsExePath))
            {
                return (simsExePath, SimsInstallType.Steam);
            }
        }

        const string eaGamesSimsPath = @"C:\Program Files\EA Games\The Sims Legacy\sims.exe";
        return File.Exists(eaGamesSimsPath) ? (eaGamesSimsPath, SimsInstallType.Ea) : null;
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
    private bool _shouldStop = false;
    private readonly Subject<bool> _hookEnabledSubject = new();
    private readonly Subject<bool> _hookDisabledSubject = new();
    private readonly Subject<bool> _simsProcExitedSubject = new();
    private readonly Subject<SimsHooked> _simsHookedSubject = new();
}
