using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using SharpHook.Native;
using SharpHook.Reactive;
using Sims1LegacyHacks.Utilities;
using Windows.Win32;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Sims1LegacyHacks.Hacks;

[SupportedOSPlatform("windows5.1.2600")]
public partial class _1080pResolutionPatch : IHack
{
    private readonly ILogger _logger;
    private readonly SimpleReactiveGlobalHook _hook;
    private SafeFileHandle? _simsHandle;
    private const int OffsetFromEntry = 0x4F5C + 2;
    private const int DesiredWidth = 1280;
    private const int DesiredHeight = 720;
    private int _previousWidth;
    private int _previousHeight;
    private int _foundAddr;

    public _1080pResolutionPatch(
        ILogger logger,
        SimpleReactiveGlobalHook hook,
        SimsProcess simsProcess
    )
    {
        _logger = logger;
        _hook = hook;
        SetupSubscriptions(simsProcess);
    }

    private void SetupKeyboardHook()
    {
        LogSetupKeyboardHooks(_logger);
        _hook.KeyReleased.Subscribe(evt =>
        {
            if (evt.RawEvent.Mask.HasCtrl() && evt.Data.KeyCode == KeyCode.VcF9)
            {
                Patch();
            }
            else if (evt.RawEvent.Mask.HasCtrl() && evt.Data.KeyCode == KeyCode.VcF8)
            {
                UnPatch();
            }
        });
    }

    private bool GetCurrentResolution()
    {
        if (_simsHandle is null)
        {
            return false;
        }
        if (
            !MemUtils.ReadPtrChain(
                _simsHandle,
                _foundAddr,
                WidthOffsetChains[0],
                out _previousWidth
            )
            || !MemUtils.ReadPtrChain(
                _simsHandle,
                _foundAddr,
                HeightOffsetChains[0],
                out _previousHeight
            )
        )
        {
            return false;
        }

        LogInitialResolution(_logger, _previousWidth, _previousHeight);
        return true;
    }

    private void SetupSubscriptions(SimsProcess simsProcess)
    {
        // simsProcess.HookEnabled.Subscribe(_ => SetupKeyboardHook());
        simsProcess.SimsProcExited.Subscribe(_ => Stop());
        var disposable = simsProcess.SimsHooked.Subscribe(evt =>
        {
            try
            {
                _simsHandle = evt.SimsHandle;
                Startup(evt.SimsHandle, evt.SimsProc);
            }
            catch (Exception ex)
            {
                LogException(_logger, ex);
                throw;
            }
        });
        simsProcess.HookDisabled.Subscribe(_ => Dispose());
    }

    private unsafe void Startup(SafeFileHandle simsHandle, Process simsProc)
    {
        var buff = new byte[4];
        fixed (byte* pBuff = buff)
        {
            if (
                !PInvoke.ReadProcessMemory(
                    simsHandle,
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

        _foundAddr = BitConverter.ToInt32(buff, 0);
        _logger.LogInformation("{FoundAddr}", _foundAddr);

        bool foundInitialResolution;
        do
        {
            foundInitialResolution = GetCurrentResolution();
            if (!foundInitialResolution)
            {
                _logger.LogDebug("InitialResolution not set");
            }
            Thread.Sleep(1000);
        } while (!foundInitialResolution);

        SetupKeyboardHook();
    }

    private void Patch()
    {
        if (_simsHandle is null)
        {
            return;
        }
        MemUtils.WritePtrChains(_simsHandle, _foundAddr, WidthOffsetChains, DesiredWidth);
        MemUtils.WritePtrChains(_simsHandle, _foundAddr, HeightOffsetChains, DesiredHeight);
        MemUtils.WritePtrChains(
            _simsHandle,
            _foundAddr,
            HeightOffsetChainsSub100,
            DesiredHeight - 100
        );
        MemUtils.WritePtrChains(
            _simsHandle,
            _foundAddr,
            HeightOffsetChainsSub150,
            DesiredHeight - 150
        );
    }

    private void UnPatch()
    {
        LogUnPatching(_logger);
        if (_simsHandle is null)
        {
            return;
        }
        MemUtils.WritePtrChains(_simsHandle, _foundAddr, WidthOffsetChains, _previousWidth);
        MemUtils.WritePtrChains(_simsHandle, _foundAddr, HeightOffsetChains, _previousHeight);
        MemUtils.WritePtrChains(
            _simsHandle,
            _foundAddr,
            HeightOffsetChainsSub100,
            _previousHeight - 100
        );
        MemUtils.WritePtrChains(
            _simsHandle,
            _foundAddr,
            HeightOffsetChainsSub150,
            _previousHeight - 150
        );
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

    public void Stop()
    {
        _simsHandle?.Dispose();
        _hook.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    [LoggerMessage(LogLevel.Information, "Registering CTRL+F9 and CTRL+F8")]
    public static partial void LogSetupKeyboardHooks(ILogger l);

    [LoggerMessage(LogLevel.Information, "CTRL+F9 pressed, patching")]
    public static partial void LogPatching(ILogger l);

    [LoggerMessage(LogLevel.Information, "CTRL+F8 pressed, un-patching")]
    public static partial void LogUnPatching(ILogger l);

    [LoggerMessage(LogLevel.Information, "Initial resolution found: {Width}x{Height}")]
    public static partial void LogInitialResolution(ILogger l, int width, int height);

    [LoggerMessage(LogLevel.Critical, "Exception")]
    public static partial void LogException(ILogger l, Exception ex);
}
