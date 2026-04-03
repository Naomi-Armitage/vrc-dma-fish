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

    public bool HasConnectedProcess => _process is not null && _process.IsValid;

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
            Logger.Debug("DMA", $"开始初始化 DMA，目标进程名 '{_config.ProcessName}'。");
            _process = ResolveProcess(_config.ProcessName);

            if (_process is null || !_process.IsValid)
            {
                Logger.Warn("DMA", BuildProcessNotFoundMessage(_config.ProcessName));
                return;
            }

            Logger.Info("DMA", $"已连接到 {_process.Name} (PID: {_process.PID})。");

            if (!_config.TryGetTargetObjectAddress(out _targetObjectAddr))
            {
                var scanner = new UnityScanner(_process);
                var gameObjectManagerAddress = 0UL;
                if (_config.TryGetGameObjectManagerAddress(out var configuredGameObjectManagerAddress))
                {
                    gameObjectManagerAddress = configuredGameObjectManagerAddress;
                    Logger.Info("DMA", $"使用配置中的 GameObjectManager 地址 0x{gameObjectManagerAddress:X}。");
                }

                _targetObjectAddr = scanner.FindObjectByName(
                    _config.TargetObjectName,
                    gameObjectManagerAddress,
                    _config.GetGameObjectManagerPatternCandidates());
            }
            else
            {
                Logger.Info("DMA", $"使用配置中的目标对象地址 0x{_targetObjectAddr:X}。");
            }

            if (_targetObjectAddr == 0)
            {
                Logger.Warn("DMA", "目标对象地址缺失，且自动扫描未找到目标对象。可在配置中填写 GameObjectManagerAddress 或 TargetObjectAddress 绕过特征扫描。");
                return;
            }

            Logger.Debug("DMA", $"最终目标对象地址 0x{_targetObjectAddr:X}。");

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

    private VmmProcess? ResolveProcess(string? configuredProcessName)
    {
        if (_vmm is null || string.IsNullOrWhiteSpace(configuredProcessName))
        {
            return null;
        }

        foreach (var candidateName in BuildProcessNameCandidates(configuredProcessName))
        {
            var process = _vmm.GetProcessByName(candidateName);
            if (process is not null && process.IsValid)
            {
                return process;
            }
        }

        return FindProcessFromSnapshot(configuredProcessName);
    }

    private VmmProcess? FindProcessFromSnapshot(string configuredProcessName)
    {
        if (_vmm is null)
        {
            return null;
        }

        var normalizedTarget = NormalizeProcessName(configuredProcessName);
        var processes = _vmm.AllProcesses ?? Array.Empty<VmmProcess>();

        var exactMatch = processes.FirstOrDefault(process =>
            process.IsValid &&
            string.Equals(NormalizeProcessName(process.Name), normalizedTarget, StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null)
        {
            return exactMatch;
        }

        return processes.FirstOrDefault(process =>
            process.IsValid &&
            NormalizeProcessName(process.Name).Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildProcessNotFoundMessage(string configuredProcessName)
    {
        if (_vmm is null)
        {
            return $"未找到进程 '{configuredProcessName}'。";
        }

        var processes = _vmm.AllProcesses ?? Array.Empty<VmmProcess>();
        var candidates = processes
            .Where(process => process.IsValid)
            .Where(process =>
            {
                var normalizedName = NormalizeProcessName(process.Name);
                var normalizedTarget = NormalizeProcessName(configuredProcessName);
                return normalizedName.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                    normalizedTarget.Contains(normalizedName, StringComparison.OrdinalIgnoreCase);
            })
            .Take(5)
            .Select(process => $"{process.Name}(PID={process.PID})")
            .ToArray();

        if (candidates.Length == 0)
        {
            return $"未找到进程 '{configuredProcessName}'。DMA 当前可见 {processes.Length} 个进程。";
        }

        return $"未找到进程 '{configuredProcessName}'。DMA 当前候选: {string.Join(", ", candidates)}。";
    }

    private static IReadOnlyList<string> BuildProcessNameCandidates(string configuredProcessName)
    {
        var trimmed = configuredProcessName.Trim();
        var candidates = new List<string>();

        void AddCandidate(string value)
        {
            if (!candidates.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(value);
            }
        }

        AddCandidate(trimmed);

        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(trimmed[..^4]);
        }
        else
        {
            AddCandidate(trimmed + ".exe");
        }

        return candidates;
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }

    public ulong ResolveGameObjectManagerAddress()
    {
        if (!HasConnectedProcess || _process is null)
        {
            return 0;
        }

        if (_config.TryGetGameObjectManagerAddress(out var configuredAddress))
        {
            return configuredAddress;
        }

        var scanner = new UnityScanner(_process);
        return scanner.FindGameObjectManager(_config.GetGameObjectManagerPatternCandidates());
    }

    public IReadOnlyList<UnityObjectInfo> DumpUnityObjects(int limit)
    {
        if (!HasConnectedProcess || _process is null)
        {
            return Array.Empty<UnityObjectInfo>();
        }

        var scanner = new UnityScanner(_process);
        var gameObjectManagerAddress = ResolveGameObjectManagerAddress();
        Logger.Debug("DMA", $"对象转储使用的 GameObjectManager 地址 0x{gameObjectManagerAddress:X}。");
        return scanner.DumpObjects(limit, gameObjectManagerAddress, _config.GetGameObjectManagerPatternCandidates());
    }
}
