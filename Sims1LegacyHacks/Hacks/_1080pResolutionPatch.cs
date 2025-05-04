using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Serilog;
using SharpHook.Native;
using SharpHook.Reactive;
using Sims1LegacyHacks.Utilities;
using Windows.Win32;

namespace Sims1LegacyHacks.Hacks;

public class _1080pResolutionPatchSettings
{
    public bool Enabled { get; set; }
    public bool PlaySound { get; set; }
    public int InitialResolutionSearchTimeout { get; set; }
}

[SupportedOSPlatform("windows5.1.2600")]
public partial class _1080pResolutionPatch : IHack
{
    private readonly ILogger _logger;
    private readonly SimpleReactiveGlobalHook _hook;
    private readonly _1080pResolutionPatchSettings _settings;
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
        SimsProcess simsProcess,
        _1080pResolutionPatchSettings settings
    )
    {
        _logger = logger;
        _hook = hook;
        _settings = settings;
        SetupSubscriptions(simsProcess);
    }

    private void SetupKeyboardHook()
    {
        if (_settings.Enabled)
        {
            _logger.Information("Registering CTRL+F9 and CTRL+F8");
            _hook.KeyReleased.Subscribe(evt =>
            {
                if (evt.RawEvent.Mask.HasCtrl() && evt.Data.KeyCode == KeyCode.VcF9)
                {
                    if (_settings.PlaySound)
                    {
                        SoundPlayer.PlaySound();
                    }
                    Patch();
                }
                else if (evt.RawEvent.Mask.HasCtrl() && evt.Data.KeyCode == KeyCode.VcF8)
                {
                    if (_settings.PlaySound)
                    {
                        SoundPlayer.PlaySound();
                    }
                    UnPatch();
                }
            });
        }
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

        _logger.Information(
            "Initial resolution found: {Width}x{Height}",
            _previousWidth,
            _previousHeight
        );
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
                Startup(evt.SimsHandle, evt.BaseAddress);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting up");
                throw;
            }
        });
        simsProcess.HookDisabled.Subscribe(_ => Dispose());
    }

    private unsafe void Startup(SafeFileHandle simsHandle, nint baseAddress)
    {
        var buff = new byte[4];
        fixed (byte* pBuff = buff)
        {
            if (
                !PInvoke.ReadProcessMemory(
                    simsHandle,
                    (void*)(baseAddress + OffsetFromEntry),
                    pBuff,
                    new UIntPtr(sizeof(int)),
                    (UIntPtr*)0
                )
            )
            {
                throw new Exception($"Error reading memory: {baseAddress} + {OffsetFromEntry}");
            }
        }

        _foundAddr = BitConverter.ToInt32(buff, 0);
        _logger.Information("{FoundAddr}", _foundAddr);

        bool foundInitialResolution;
        do
        {
            foundInitialResolution = GetCurrentResolution();
            if (!foundInitialResolution)
            {
                _logger.Debug(
                    "InitialResolution not set, sleeping {Sleep}",
                    _settings.InitialResolutionSearchTimeout
                );
            }
            Thread.Sleep(_settings.InitialResolutionSearchTimeout);
        } while (
            !foundInitialResolution && (!_simsHandle?.IsInvalid ?? false) && !_hook.IsDisposed
        );

        if ((_simsHandle?.IsInvalid ?? false) || _hook.IsDisposed)
        {
            return;
        }
        SetupKeyboardHook();
    }

    private void Patch()
    {
        if (_simsHandle is null)
        {
            return;
        }
        _logger.Information("CTRL+F9 pressed, patching");
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
        _logger.Information("CTRL+F8 pressed, un-patching");
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
        [0x44, 0x14], // (pause text)
        [0x44, 0x24], // (pause text)
        [0x44, 0xDC], // (left control thing)
        [0x44, 0x44, 0xDC],
        [0x4C, 0x10, 0x90, 0x14],
        [0x4C, 0x10, 0x90, 0x24],
        [0x4C, 0x10, 0x90, 0xDC], // (panelback)
        [0x44, 0x150, 0x90, 0x134, 0xDC], // (catalog page arrows + popup description box)
        [0x44, 0x150, 0x90, 0x134, 0xEC], // (catalog page arrows + popup description box)
        [0x44, 0x1A8, 0x14], // (edge scroll detection)
        [0x44, 0x1A8, 0x1C], // (edge scroll detection)
    ];
    private static readonly List<List<int>> HeightOffsetChains =
    [
        [0x4, 0x44],
        [0x44, 0x44, 0xE0],
        [0x44, 0x18], // (pause text)
        [0x44, 0x28], // (pause text)
        [0x44, 0xE0], // (left control thing)
        [0x4C, 0x10, 0x90, 0x18],
        [0x4C, 0x10, 0x90, 0x28],
        [0x4C, 0x10, 0x90, 0xE0], // (panelback)
        [0x44, 0x1A8, 0x18], // (edge scroll detection)
        [0x44, 0x1A8, 0x20], // (edge scroll detection)
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
}
