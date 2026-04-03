using System.Runtime.Versioning;
using Vmmsharp;
using VrcDmaFish.Models;
using VrcDmaFish.UI;

namespace VrcDmaFish.Providers;

[SupportedOSPlatform("windows")]
public sealed class DmaProvider : IFishSignalSource, IDisposable
{
    private const int PositionDisappearFramesForCatch = 2;
    private readonly SignalSourceConfig _config;
    private Vmm? _vmm;
    private VmmProcess? _process;
    private ulong _targetObjectAddr;
    private SignalOffsets? _offsets;
    private PositionOffsets? _positionOffsets;
    private BarRangeOffsets? _barRangeOffsets;
    private ulong _fallbackTensionOffset = 0x40;
    private bool _sawPositionData;
    private int _missingPositionFrames;

    public DmaProvider(SignalSourceConfig config)
    {
        _config = config;
        Initialize();
    }

    public bool IsReady { get; private set; }

    private void Initialize()
    {
        try
        {
            var nativeLibraryPath = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(nativeLibraryPath, "vmm.dll")))
            {
                Vmm.LoadNativeLibrary(nativeLibraryPath);
            }

            _vmm = new Vmm("-device", "fpga");
            _process = _vmm.GetProcessByName(_config.ProcessName);

            if (_process is null || !_process.IsValid)
            {
                Logger.Warn("DMA", $"未找到进程 '{_config.ProcessName}'。");
                return;
            }

            Logger.Info("DMA", $"已连接到 {_process.Name} (PID: {_process.PID})。");

            if (!_config.TryGetTargetObjectAddress(out _targetObjectAddr))
            {
                var scanner = new UnityScanner(_process);
                _targetObjectAddr = scanner.FindObjectByName(_config.TargetObjectName);
            }

            if (_targetObjectAddr == 0)
            {
                Logger.Warn("DMA", "目标对象地址缺失，且自动扫描未找到目标对象。");
                return;
            }

            if (_config.TryGetSignalOffsets(out var offsets))
            {
                _offsets = offsets;
                Logger.Info(
                    "DMA",
                    $"使用配置中的偏移：hooked=0x{offsets.HookedOffset:X}, catch=0x{offsets.CatchCompletedOffset:X}, tension=0x{offsets.TensionOffset:X}。");
            }
            else
            {
                if (_config.TryGetTensionOffset(out var configuredTensionOffset))
                {
                    _fallbackTensionOffset = configuredTensionOffset;
                }

                Logger.Warn(
                    "DMA",
                    $"缺少 hooked/catch 偏移，已回退到仅张力模式，使用偏移 0x{_fallbackTensionOffset:X}。");
            }

            if (_config.TryGetPositionOffsets(out var positionOffsets))
            {
                _positionOffsets = positionOffsets;
                Logger.Info(
                    "DMA",
                    $"已启用位置控制偏移：fish=0x{positionOffsets.FishPositionOffset:X}, barCenter=0x{positionOffsets.BarCenterOffset:X}, barHeight=0x{positionOffsets.BarHeightOffset:X}。");
            }
            else if (_config.TryGetBarRangeOffsets(out var barRangeOffsets))
            {
                _barRangeOffsets = barRangeOffsets;
                Logger.Info(
                    "DMA",
                    $"已启用位置控制偏移：fish=0x{barRangeOffsets.FishPositionOffset:X}, barTop=0x{barRangeOffsets.BarTopOffset:X}, barBottom=0x{barRangeOffsets.BarBottomOffset:X}。");
            }
            else
            {
                Logger.Warn("DMA", "未提供鱼位置 / 白条位置偏移，将继续使用张力模式。");
            }

            IsReady = true;
        }
        catch (Exception ex)
        {
            Logger.Error("DMA", $"初始化失败: {ex.Message}");
        }
    }

    public FishContext Read()
    {
        if (!IsReady || _process is null)
        {
            return new FishContext();
        }

        try
        {
            var context = new FishContext();

            if (_offsets is SignalOffsets offsets)
            {
                context.IsHooked = ReadValue<byte>(_targetObjectAddr + offsets.HookedOffset) != 0;
                context.CatchCompleted = ReadValue<byte>(_targetObjectAddr + offsets.CatchCompletedOffset) != 0;
                context.Tension = Math.Clamp(ReadValue<float>(_targetObjectAddr + offsets.TensionOffset), 0f, 1f);
            }
            else
            {
                context.Tension = Math.Clamp(ReadValue<float>(_targetObjectAddr + _fallbackTensionOffset), 0f, 1f);
                context.IsHooked = context.Tension > 0.05f;
                context.CatchCompleted = context.Tension >= 0.99f;
            }

            var hasPositionData = TryReadPositionData(context);
            if (hasPositionData)
            {
                _sawPositionData = true;
                _missingPositionFrames = 0;

                if (_offsets is null)
                {
                    context.IsHooked = true;
                }

                if (_offsets is null)
                {
                    context.Tension = InferTension(context);
                }
            }
            else if (_sawPositionData)
            {
                _missingPositionFrames++;
                if (_offsets is null)
                {
                    context.IsHooked = true;
                    context.CatchCompleted = _missingPositionFrames >= PositionDisappearFramesForCatch;
                }
            }

            return context;
        }
        catch (Exception ex)
        {
            Logger.Error("DMA", $"读取失败: {ex.Message}");
            IsReady = false;
            return new FishContext();
        }
    }

    public void ResetCycle()
    {
        _sawPositionData = false;
        _missingPositionFrames = 0;
    }

    public void Dispose()
    {
        IsReady = false;
        _vmm?.Dispose();
    }

    private T ReadValue<T>(ulong address) where T : unmanaged
    {
        if (_process is null)
        {
            throw new InvalidOperationException("DMA 进程尚未初始化。");
        }

        return _process.MemReadAs<T>(address, Vmm.FLAG_NOCACHE)
            ?? throw new InvalidOperationException($"读取 0x{address:X} 处的 {typeof(T).Name} 失败。");
    }

    private bool TryReadPositionData(FishContext context)
    {
        if (_positionOffsets is PositionOffsets directOffsets)
        {
            context.FishCenterY = ReadValue<float>(_targetObjectAddr + directOffsets.FishPositionOffset);
            context.BarCenterY = ReadValue<float>(_targetObjectAddr + directOffsets.BarCenterOffset);
            context.BarHeight = Math.Abs(ReadValue<float>(_targetObjectAddr + directOffsets.BarHeightOffset));
            return context.HasPositionData;
        }

        if (_barRangeOffsets is BarRangeOffsets rangeOffsets)
        {
            var fishCenterY = ReadValue<float>(_targetObjectAddr + rangeOffsets.FishPositionOffset);
            var barTop = ReadValue<float>(_targetObjectAddr + rangeOffsets.BarTopOffset);
            var barBottom = ReadValue<float>(_targetObjectAddr + rangeOffsets.BarBottomOffset);
            var barHeight = Math.Abs(barBottom - barTop);

            context.FishCenterY = fishCenterY;
            context.BarCenterY = (barTop + barBottom) / 2f;
            context.BarHeight = barHeight;
            return context.HasPositionData;
        }

        return false;
    }

    private static float InferTension(FishContext context)
    {
        if (!context.HasPositionData)
        {
            return context.Tension;
        }

        var normalizedDistance = Math.Abs(context.FishCenterY!.Value - context.BarCenterY!.Value) / Math.Max(context.BarHeight!.Value * 0.5f, 0.0001f);
        return Math.Clamp(normalizedDistance, 0f, 1f);
    }
}
