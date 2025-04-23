using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using PatternFinder;
using Sims1LegacyHacks.Utilities;
using Windows.Win32;
using Windows.Win32.System.Memory;

namespace Sims1LegacyHacks.Hacks;

public class DebugCheatsSettings
{
    public bool Enabled { get; set; }
    public bool PlaySound { get; set; }
}

[SupportedOSPlatform("windows5.1.2600")]
public partial class DebugCheats : IHack
{
    private readonly ILogger _logger;
    private readonly DebugCheatsSettings _settings;
    private SafeFileHandle? _simsHandle;
    private Process? _simsProcess;

    public DebugCheats(ILogger logger, SimsProcess simsProcess, DebugCheatsSettings settings)
    {
        _logger = logger;
        _settings = settings;
        SetupSubscriptions(simsProcess);
    }

    private void SetupSubscriptions(SimsProcess simsProcess)
    {
        if (_settings.Enabled)
        {
            LogEnabled(_logger);
            simsProcess.SimsHooked.Subscribe(evt =>
            {
                _simsHandle = evt.SimsHandle;
                _simsProcess = evt.SimsProcess;
                ReadAndPatchMemory();
            });
        }
    }

    private unsafe void ReadAndPatchMemory()
    {
        if (_simsProcess is not null)
        {
            LogSearchingMemoryForDebugCheats(_logger);
            var addr = _simsProcess.MainModule!.BaseAddress;
            var maxSize = _simsProcess.VirtualMemorySize64;
            var buff = new byte[4096];
            fixed (byte* pBuff = buff)
            {
                while (addr < maxSize)
                {
                    if (maxSize - addr < buff.Length)
                    {
                        buff = new byte[maxSize - addr];
                    }

                    if (
                        !PInvoke.ReadProcessMemory(
                            _simsHandle,
                            (void*)addr,
                            pBuff,
                            new UIntPtr((uint)buff.Length),
                            (UIntPtr*)0
                        )
                    )
                    {
                        var err = Marshal.GetLastWin32Error();
                        LogException(_logger, "ReadProcessMemory failed", err);
                        return;
                    }
                    if (Pattern.Find(buff, PatternBytes, out var offset))
                    {
                        LogFoundOffset(_logger, addr, offset);

                        if (
                            !PInvoke.VirtualProtectEx(
                                _simsHandle,
                                (void*)addr,
                                new UIntPtr((uint)SearchPattern.Length),
                                PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE,
                                out var prevFlags
                            )
                        )
                        {
                            var err = Marshal.GetLastWin32Error();
                            LogException(_logger, "VirtualProtectEx failed", err);
                            return;
                        }

                        fixed (int* pPatchBytesBuff = PatchBytes)
                        {
                            if (
                                !PInvoke.WriteProcessMemory(
                                    _simsHandle,
                                    (void*)(addr + offset + 4),
                                    pPatchBytesBuff,
                                    new UIntPtr((uint)PatchBytes.Length),
                                    (UIntPtr*)0
                                )
                            )
                            {
                                var err = Marshal.GetLastWin32Error();
                                LogException(_logger, "WriteProcessMemory failed", err);
                                return;
                            }
                        }

                        if (
                            !PInvoke.VirtualProtectEx(
                                _simsHandle,
                                (void*)addr,
                                new UIntPtr((uint)SearchPattern.Length),
                                prevFlags,
                                out prevFlags
                            )
                        )
                        {
                            var err = Marshal.GetLastWin32Error();
                            LogException(_logger, "VirtualProtectEx failed", err);
                            return;
                        }

                        LogSuccess(_logger);
                        if (_settings.PlaySound)
                        {
                            SoundPlayer.PlaySound();
                        }
                        break;
                    }

                    addr += buff.Length;
                }
            }
        }
    }

    public void Dispose()
    {
        _simsHandle?.Dispose();
        _simsProcess?.Dispose();
    }

    [LoggerMessage(LogLevel.Information, "Debug cheats are enabled")]
    public static partial void LogEnabled(ILogger l);

    [LoggerMessage(LogLevel.Information, "Searching Sims memory for debug cheats bytes")]
    public static partial void LogSearchingMemoryForDebugCheats(ILogger l);

    [LoggerMessage(LogLevel.Information, "Found debug cheats bytes at {Address} + {Offset}")]
    public static partial void LogFoundOffset(ILogger l, nint address, long offset);

    [LoggerMessage(LogLevel.Error, "{Msg}\nWin32 Error: {Win32Err}")]
    public static partial void LogException(ILogger l, string msg, int win32Err);

    [LoggerMessage(LogLevel.Information, "Debug cheats bytes patched successfully")]
    public static partial void LogSuccess(ILogger l);

    private const string SearchPattern = "807B4C007543";
    private static readonly int[] PatchBytes = [0xEB];
    private static readonly PatternFinder.Pattern.Byte[] PatternBytes = Pattern.Transform(
        SearchPattern
    );
}
