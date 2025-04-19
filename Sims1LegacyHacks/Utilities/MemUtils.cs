using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Sims1LegacyHacks.Utilities;

public static class MemUtils
{
    [SupportedOSPlatform("windows5.1.2600")]
    public static void ReadChains(
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

    [SupportedOSPlatform("windows5.1.2600")]
    public static unsafe bool WritePtrChains(
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
    public static unsafe bool ReadPtrChain(
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
