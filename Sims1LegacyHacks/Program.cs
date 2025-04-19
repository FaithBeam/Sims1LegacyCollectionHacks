using System.Diagnostics;
using System.Runtime.Versioning;
using SharpHook.Reactive;
using Sims1LegacyHacks.Hacks;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace Sims1LegacyHacks;

internal static class Program
{
    [SupportedOSPlatform("windows5.1.2600")]
    public static void Main(string[] args)
    {
        var simsProc = Process.GetProcessesByName("Sims").SingleOrDefault();
        if (simsProc is null)
        {
            throw new Exception(
                "Cannot find Sims.exe in the running processes. Is Sims.exe running?"
            );
        }

        var simsHandle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS,
            false,
            (uint)simsProc.Id
        );
        if (simsHandle.IsInvalid)
        {
            throw new Exception($"Error opening process pid: {simsProc.Id}");
        }

        var hook = new SimpleReactiveGlobalHook();

        var _1080pPatch = new _1080pResolutionPatch(hook, simsHandle, simsProc);

        hook.Run();
    }
}
