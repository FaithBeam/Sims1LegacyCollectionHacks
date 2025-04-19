using System.Diagnostics;
using System.Runtime.Versioning;
using SharpHook.Native;
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

        var _1080pFix = new _1080pResolutionFix(simsHandle, simsProc);

        var hook = new SimpleReactiveGlobalHook();
        hook.KeyReleased.Subscribe(evt =>
        {
            if (evt.RawEvent.Mask.HasCtrl() && evt.Data.KeyCode == KeyCode.VcF9)
            {
                _1080pFix.RunFix();
            }
            else if (evt.RawEvent.Mask.HasCtrl() && evt.Data.KeyCode == KeyCode.VcF8)
            {
                _1080pFix.UndoFix();
            }
        });
        hook.Run();
    }
}
