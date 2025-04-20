using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpHook.Reactive;
using Sims1LegacyHacks.Hacks;

namespace Sims1LegacyHacks;

internal partial class Program
{
    [SupportedOSPlatform("windows5.1.2600")]
    public static void Main(string[] args)
    {
        using var logFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Trace)
        );
        var logger = logFactory.CreateLogger<Program>();

        var hook = new SimpleReactiveGlobalHook();

        var simsProc = new SimsProcess(logFactory.CreateLogger<SimsProcess>());
        simsProc.Start();

        var _1080pPatch = new _1080pResolutionPatch(
            logFactory.CreateLogger<_1080pResolutionPatch>(),
            hook,
            simsProc
        );

        hook.Run();
    }

    [LoggerMessage(LogLevel.Information, "Searching processes for Sims.exe.")]
    public static partial void LogGetSimsProc(ILogger l);

    [LoggerMessage(LogLevel.Critical, "Unable to find Sims.exe process.")]
    public static partial void LogGetSimsProcFailed(ILogger l);
}
