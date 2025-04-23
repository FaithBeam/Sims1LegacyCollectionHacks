using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharpHook.Reactive;
using Sims1LegacyHacks.Hacks;
using Sims1LegacyHacks.Utilities;

namespace Sims1LegacyHacks;

internal partial class Program
{
    [SupportedOSPlatform("windows5.1.2600")]
    public static void Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        // SoundPlayer.LogAvailableResources();
        using var logFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Trace)
        );
        var logger = logFactory.CreateLogger<Program>();

        var hook = new SimpleReactiveGlobalHook();

        var simsProcessSettings = configuration
            .GetRequiredSection("simsProcess")
            .Get<SimsProcessSettings>();
        var simsProc = new SimsProcess(
            logFactory.CreateLogger<SimsProcess>(),
            simsProcessSettings!
        );
        simsProc.Start();

        var debugCheatsSettings = configuration
            .GetSection("hacks:debugCheats")
            .Get<DebugCheatsSettings>();
        if (debugCheatsSettings is not null)
        {
            var debugCheats = new DebugCheats(
                logFactory.CreateLogger<DebugCheats>(),
                simsProc,
                debugCheatsSettings
            );
        }

        var _1080pPatchSettings = configuration
            .GetSection("hacks:1080pPatch")
            .Get<_1080pResolutionPatchSettings>();
        if (_1080pPatchSettings is not null)
        {
            var _1080pPatch = new _1080pResolutionPatch(
                logFactory.CreateLogger<_1080pResolutionPatch>(),
                hook,
                simsProc,
                _1080pPatchSettings
            );
        }

        hook.Run();
    }

    [LoggerMessage(LogLevel.Information, "Searching processes for Sims.exe.")]
    public static partial void LogGetSimsProc(ILogger l);

    [LoggerMessage(LogLevel.Critical, "Unable to find Sims.exe process.")]
    public static partial void LogGetSimsProcFailed(ILogger l);
}
