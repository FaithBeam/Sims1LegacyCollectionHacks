using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Serilog;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace Sims1LegacyHacks;

public class SimsHooked(
    Process simsProcess,
    SafeFileHandle simsHandle,
    string? simsPath,
    SimsInstallType? simsInstallType
)
{
    public nint BaseAddress { get; } = simsProcess.MainModule!.BaseAddress;
    public SafeFileHandle SimsHandle { get; } = simsHandle;
    public Process SimsProcess { get; } = simsProcess;
    public string? SimsPath { get; set; } = simsPath;
    public SimsInstallType? SimsInstallType { get; set; } = simsInstallType;
}

public class SimsProcessSettings
{
    public string? SimsPath { get; set; }
    public SimsInstallType? SimsInstallType { get; set; }
    public bool AutoStart { get; set; }
    public int MonitorInterval { get; set; } = 1000;
}

[SupportedOSPlatform("windows5.1.2600")]
public partial class SimsProcess(ILogger logger, SimsProcessSettings settings) : IDisposable
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
        _logger.Information("Monitoring processes for Sims.exe");
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

            _logger.Information("Found Sims process: {Process}", simsProc);
            _logger.Information("Sims path: {Path}", simsProc.MainModule!.FileName);

            simsProc.EnableRaisingEvents = true;
            simsProc.Exited += SimsProcOnExited;

            string? simsPathForEvt = null;
            SimsInstallType? simsInstallTypeForEvt = null;
            foreach (ProcessModule module in simsProc.Modules)
            {
                if (module.ModuleName == "Sims.exe")
                {
                    simsPathForEvt = module.FileName;
                }

                simsInstallTypeForEvt = module.ModuleName switch
                {
                    "steam_api.dll" => SimsInstallType.Steam,
                    "Activation.dll" => SimsInstallType.Ea,
                    _ => simsInstallTypeForEvt,
                };
            }

            _logger.Information("Attempting to retrieve a handle to {Pid}", simsProc.Id);
            var simsHandle = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS,
                false,
                (uint)simsProc.Id
            );
            if (simsHandle.IsInvalid)
            {
                throw new Exception($"Error opening process pid: {simsProc.Id}");
            }

            _logger.Information(
                "Retrieving a handle successful, handle valid: {HandleValid}",
                !simsHandle.IsInvalid
            );
            _logger.Information("Stop monitoring processes for Sims.exe");
            _simsHookedSubject.OnNext(
                new SimsHooked(simsProc, simsHandle, simsPathForEvt, simsInstallTypeForEvt)
            );
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
        _logger.Information("Sims.exe has exited");
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

    private Thread? _simsThread;
    private readonly ILogger _logger = logger;
    private readonly SimsProcessSettings _settings = settings;
    private bool _shouldStop = false;
    private readonly Subject<bool> _hookEnabledSubject = new();
    private readonly Subject<bool> _hookDisabledSubject = new();
    private readonly Subject<bool> _simsProcExitedSubject = new();
    private readonly Subject<SimsHooked> _simsHookedSubject = new();
}
