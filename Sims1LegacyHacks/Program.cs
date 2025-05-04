using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Serilog;
using SharpHook.Reactive;
using Sims1LegacyHacks.Hacks;

namespace Sims1LegacyHacks;

internal class Program
{
    [SupportedOSPlatform("windows5.1.2600")]
    public static void Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.File(
                "Sims1LegacyHacks.log",
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        var logger = Log.Logger.ForContext<Program>();
        try
        {
            logger.Information(
                "Operating System: {OSVersion}",
                Environment.OSVersion.VersionString
            );

            var hook = new SimpleReactiveGlobalHook();

            var simsProcessSettings = configuration
                .GetRequiredSection("simsProcess")
                .Get<SimsProcessSettings>();
            SimsProcess? simsProc = null;
            try
            {
                simsProc = new SimsProcess(Log.Logger, simsProcessSettings!);
                simsProc.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            var debugCheatsSettings = configuration
                .GetSection("hacks:debugCheats")
                .Get<DebugCheatsSettings>();
            if (debugCheatsSettings is not null)
            {
                var debugCheats = new DebugCheats(Log.Logger, simsProc, debugCheatsSettings);
            }

            var _1080pPatchSettings = configuration
                .GetSection("hacks:1080pPatch")
                .Get<_1080pResolutionPatchSettings>();
            if (_1080pPatchSettings is not null)
            {
                var _1080pPatch = new _1080pResolutionPatch(
                    Log.Logger,
                    hook,
                    simsProc,
                    _1080pPatchSettings
                );
            }

            hook.Run();
        }
        catch (Exception e)
        {
            logger.Error(e, "Exception");
            Log.CloseAndFlush();
            throw;
        }
    }
}
