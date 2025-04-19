using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Sims1LegacyHacks.Utilities;
using Windows.Win32;

namespace Sims1LegacyHacks.Hacks;

[SupportedOSPlatform("windows5.1.2600")]
public class _1080pResolutionFix
{
    private readonly SafeFileHandle _simsHandle;
    private readonly Process _simsProc;
    private const int OffsetFromEntry = 0x4F5C + 2;
    private const int DesiredWidth = 1280;
    private const int DesiredHeight = 720;
    private int _previousWidth;
    private int _previousHeight;
    private int _foundAddr;

    public _1080pResolutionFix(SafeFileHandle simsHandle, Process simsProc)
    {
        _simsHandle = simsHandle;
        _simsProc = simsProc;
        Startup(simsHandle, simsProc);
    }

    private void GetCurrentResolution()
    {
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
            throw new Exception("Error reading previous width or height");
        }
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

        GetCurrentResolution();
    }

    public void UndoFix()
    {
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

    public void RunFix()
    {
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
}
