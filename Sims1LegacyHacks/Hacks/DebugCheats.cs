using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using PatternFinder;
using Sims1LegacyHacks.Utilities;
using Windows.Win32;
using Windows.Win32.System.Memory;
using ILogger = Serilog.ILogger;

namespace Sims1LegacyHacks.Hacks;

public class DebugCheatsSettings
{
    public bool Enabled { get; set; }
    public bool PlaySound { get; set; }
}

[SupportedOSPlatform("windows5.1.2600")]
public class DebugCheats : IHack
{
    private readonly ILogger _logger;
    private readonly DebugCheatsSettings _settings;
    private SafeFileHandle? _simsHandle;
    private Process? _simsProcess;

    public DebugCheats(ILogger logger, SimsProcess simsProcess, DebugCheatsSettings settings)
    {
        _logger = logger.ForContext<DebugCheats>();
        _settings = settings;
        SetupSubscriptions(simsProcess);
    }

    private void SetupSubscriptions(SimsProcess simsProcess)
    {
        if (_settings.Enabled)
        {
            _logger.Information("Debug cheats are enabled");
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
            _logger.Information("Searching Sims memory for debug cheats bytes");
            var addr = _simsProcess.MainModule!.BaseAddress;
            var maxSize = _simsProcess.VirtualMemorySize64;
            _logger.Information("Addr: {Addr}, MaxSize: {MaxSize}", addr, maxSize);
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
                        _logger.Error(
                            "ReadProcessMemory failed, handle: {Handle}, address: {Address}, Win32 Error: {Win32Err}",
                            _simsHandle,
                            addr,
                            err
                        );
                        throw new DebugCheatsException(
                            $"ReadProcessMemory failed, handle: {_simsHandle}, address: {addr}, win32Err: {err}"
                        );
                    }
                    if (Pattern.Find(buff, PatternBytes, out var offset))
                    {
                        _logger.Information(
                            "Found debug cheats bytes at {Address} + {Offset}",
                            addr,
                            offset
                        );

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
                            _logger.Error("VirtualProtectEx failed, Win32 Error: {Win32Err}", err);
                            throw new DebugCheatsException(
                                $"VirtualProtectEx failed, win32Err: {err}"
                            );
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
                                _logger.Error(
                                    "WriteProcessMemory failed, handle: {SimsHandle}, address: {Address} + {Offset} + 4, Win32 Error: {Win32Err}",
                                    _simsHandle,
                                    addr,
                                    offset,
                                    err
                                );
                                throw new DebugCheatsException(
                                    $"WriteProcessMemory failed, handle: {_simsHandle}, address: {addr} + {offset} + 4, win32Err: {err}"
                                );
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
                            _logger.Error(
                                "{Msg} Win32 Error: {Win32Err}",
                                "VirtualProtectEx failed",
                                err
                            );
                            throw new DebugCheatsException(
                                $"VirtualProtectEx failed, win32Err: {err}"
                            );
                        }

                        _logger.Information("Debug cheats bytes patched successfully");
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

    private const string SearchPattern = "807B4C007543";
    private static readonly int[] PatchBytes = [0xEB];
    private static readonly PatternFinder.Pattern.Byte[] PatternBytes = Pattern.Transform(
        SearchPattern
    );
}

public class DebugCheatsException : Exception
{
    public DebugCheatsException() { }

    public DebugCheatsException(string message)
        : base(message) { }

    public DebugCheatsException(string message, Exception inner)
        : base(message, inner) { }
}
