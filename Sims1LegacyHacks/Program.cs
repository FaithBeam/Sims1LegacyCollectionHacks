using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using SharpHook.Native;
using SharpHook.Reactive;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Sims1LegacyHacks;

internal static class Program
{
    [SupportedOSPlatform("windows5.1.2600")]
    public static unsafe void Main(string[] args)
    {
        var simsProc = Process.GetProcessesByName("Sims").SingleOrDefault();
        if (simsProc is null)
        {
            throw new Exception(
                "Cannot find Sims.exe in the running processes. Is Sims.exe running?"
            );
        }

        var _simsHandle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_ALL_ACCESS,
            false,
            (uint)simsProc.Id
        );
        if (_simsHandle.IsInvalid)
        {
            throw new Exception($"Error opening process pid: {simsProc.Id}");
        }

        var buff = new byte[4];
        fixed (byte* pBuff = buff)
        {
            if (
                !PInvoke.ReadProcessMemory(
                    _simsHandle,
                    (void*)(simsProc.MainModule!.BaseAddress + OffsetFromEntry),
                    pBuff,
                    new UIntPtr(sizeof(int)),
                    (UIntPtr*)0
                )
            )
            {
                throw new Exception(
                    $"Error reading memory: {simsProc.MainModule!.BaseAddress} + {OffsetFromEntry}"
                );
            }
        }
        var masterObjAddr = BitConverter.ToInt32(buff, 0);
        if (
            !ReadPtrChain(_simsHandle, masterObjAddr, WidthOffsetChains[0], out _previousWidth)
            || !ReadPtrChain(_simsHandle, masterObjAddr, HeightOffsetChains[0], out _previousHeight)
        )
        {
            throw new Exception("Error reading previous width or height");
        }

        var hook = new SimpleReactiveGlobalHook();
        hook.KeyReleased.Subscribe(evt =>
        {
            if (evt.RawEvent.Mask.HasCtrl() && evt.Data.KeyCode == KeyCode.VcF9)
            {
                RunFix(_simsHandle, masterObjAddr);
            }
            else if (evt.RawEvent.Mask.HasCtrl() && evt.Data.KeyCode == KeyCode.VcF8)
            {
                UndoFix(_simsHandle, masterObjAddr);
            }
        });
        hook.Run();
    }

    private const int OffsetFromEntry = 0x4F5C + 2;
    private const int DesiredWidth = 1280;
    private const int DesiredHeight = 720;
    private static int _previousWidth;
    private static int _previousHeight;

    [SupportedOSPlatform("windows5.1.2600")]
    private static void UndoFix(SafeFileHandle simsHandle, int uberVal)
    {
        WritePtrChains(simsHandle, uberVal, WidthOffsetChains, _previousWidth);
        WritePtrChains(simsHandle, uberVal, HeightOffsetChains, _previousHeight);
        WritePtrChains(simsHandle, uberVal, HeightOffsetChainsSub100, _previousHeight - 100);
        WritePtrChains(simsHandle, uberVal, HeightOffsetChainsSub150, _previousHeight - 150);
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static void RunFix(SafeFileHandle simsHandle, int uberVal)
    {
        WritePtrChains(simsHandle, uberVal, WidthOffsetChains, DesiredWidth);
        WritePtrChains(simsHandle, uberVal, HeightOffsetChains, DesiredHeight);
        WritePtrChains(simsHandle, uberVal, HeightOffsetChainsSub100, DesiredHeight - 100);
        WritePtrChains(simsHandle, uberVal, HeightOffsetChainsSub150, DesiredHeight - 150);
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static void ReadChains(
        SafeHandle handle,
        nint addr,
        IEnumerable<IEnumerable<IEnumerable<int>>> lols
    )
    {
        foreach (var lol in lols)
        {
            foreach (var offsetChain in lol)
            {
                if (!ReadPtrChain(handle, addr, offsetChain, out var result))
                {
                    throw new Exception("Sims.exe not found");
                }
                Console.WriteLine(result);
            }
        }
    }

    private static readonly List<List<int>> WidthOffsetChains =
    [
        [0x4, 0x30],
        [0x4, 0x40],
        [0x44, 0x14], // width (pause text)
        [0x44, 0x24], // width (pause text)
        [0x44, 0xDC], // width (left control thing)
        [0x44, 0x44, 0xDC],
        [0x4C, 0x10, 0x90, 0x14],
        [0x4C, 0x10, 0x90, 0x24],
        [0x4C, 0x10, 0x90, 0xDC], // width (panelback)
    ];
    private static readonly List<List<int>> HeightOffsetChains =
    [
        [0x4, 0x44],
        [0x44, 0x44, 0xE0],
        [0x44, 0x18], // height (pause text)
        [0x44, 0x28], // height (pause text)
        [0x44, 0xE0], // height (left control thing)
        [0x4C, 0x10, 0x90, 0x18],
        [0x4C, 0x10, 0x90, 0x28],
        [0x4C, 0x10, 0x90, 0xE0], // height (panelback)
    ];

    private static readonly List<List<int>> HeightOffsetChainsSub100 =
    [
        [0x4, 0x34],
    ];
    private static readonly List<List<int>> HeightOffsetChainsSub150 =
    [
        [0x4C, 0x10, 0x90, 0x10],
        [0x4C, 0x10, 0x90, 0x20],
        [0x4C, 0x10, 0x90, 0xD8], // height (panelback -150)
        [0x4C, 0x10, 0x90, 0xE8], // height (idk -150)
    ];

    [SupportedOSPlatform("windows5.1.2600")]
    private static unsafe bool WritePtrChains(
        SafeFileHandle handle,
        nint addr,
        List<List<int>> listOfOffsets,
        int valToWrite
    )
    {
        foreach (var offsets in listOfOffsets)
        {
            var buff = 0;

            if (
                !PInvoke.ReadProcessMemory(
                    handle,
                    (void*)addr,
                    &buff,
                    new UIntPtr(sizeof(int)),
                    (UIntPtr*)0
                )
            )
            {
                throw new Exception($"Error reading process memory: {addr}");
            }

            foreach (var offset in offsets[..^1])
            {
                if (
                    !PInvoke.ReadProcessMemory(
                        handle,
                        (void*)(buff + offset),
                        &buff,
                        new UIntPtr(sizeof(int)),
                        (UIntPtr*)0
                    )
                )
                {
                    throw new Exception($"Error reading process memory: {buff} + {offset}");
                }
            }

            if (
                !PInvoke.WriteProcessMemory(
                    (HANDLE)handle.DangerousGetHandle(),
                    (void*)(buff + offsets.Last()),
                    &valToWrite,
                    new UIntPtr(sizeof(int))
                )
            )
            {
                return false;
            }
        }
        return true;
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static unsafe bool ReadPtrChain(
        SafeHandle handle,
        nint addr,
        IEnumerable<int> offsets,
        out int result
    )
    {
        var buff = 0;

        if (
            !PInvoke.ReadProcessMemory(
                handle,
                (void*)addr,
                &buff,
                new UIntPtr(sizeof(int)),
                (UIntPtr*)0
            )
        )
        {
            result = -1;
            return false;
        }

        foreach (var offset in offsets)
        {
            if (
                !PInvoke.ReadProcessMemory(
                    handle,
                    (void*)(buff + offset),
                    &buff,
                    new UIntPtr(sizeof(int)),
                    (UIntPtr*)0
                )
            )
            {
                result = -1;
                return false;
            }
        }

        result = buff;
        return true;
    }
}
