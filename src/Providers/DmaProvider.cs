using System.Runtime.Versioning;
using System.Runtime.InteropServices;
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
    private IProcessMemory? _memory;
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

    public bool HasConnectedProcess => _memory is not null && _memory.IsValid;

    public string? ConnectedProcessName => _memory?.Name;

    public int? ConnectedProcessId => _memory?.ProcessId;

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
            _memory = ResolveProcessMemory(_config.ProcessName);

            if (_memory is null || !_memory.IsValid)
            {
                Logger.Warn("DMA", BuildProcessNotFoundMessage(_config.ProcessName));
                return;
            }

            Logger.Info("DMA", $"已连接到 {_memory.Name} (PID: {_memory.ProcessId})。");

            if (!TryResolveConfiguredAddress(_config.TargetObjectAddress, out _targetObjectAddr, out var configuredTargetObjectDescription))
            {
                var scanner = new UnityScanner(_memory, _config.GetUnityNativeLayout());
                var gameObjectManagerAddress = 0UL;
                _config.TryGetTargetKlassAddress(out var targetKlassAddress);
                if (TryResolveConfiguredAddress(_config.GameObjectManagerAddress, out var configuredGameObjectManagerAddress, out var configuredGameObjectManagerDescription))
                {
                    gameObjectManagerAddress = configuredGameObjectManagerAddress;
                    Logger.Debug("DMA", $"GameObjectManager address source: {configuredGameObjectManagerDescription}");
                    Logger.Info("DMA", $"使用配置中的 GameObjectManager 地址 0x{gameObjectManagerAddress:X}。");
                }

                _targetObjectAddr = scanner.FindTargetObject(
                    _config.TargetObjectName,
                    targetKlassAddress,
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
            ResolvedIl2CppInspectorLayout? il2CppLayout = null;
            Il2CppInspectorFieldSelection? il2CppSelection = null;

            if (_config.TryGetIl2CppInspectorProSelection(out var configuredIl2CppSelection))
            {
                il2CppSelection = configuredIl2CppSelection;
                var il2CppResolver = new Il2CppInspectorProResolver();
                if (il2CppResolver.TryResolve(configuredIl2CppSelection, out var resolvedLayout, out var failureReason))
                {
                    il2CppLayout = resolvedLayout;
                    Logger.Info(
                        "DMA",
                        $"已加载 Il2CppInspectorPro 布局：type={resolvedLayout.TypeName}, source={resolvedLayout.SourcePath}。");
                }
                else
                {
                    Logger.Warn("DMA", $"Il2CppInspectorPro 自动偏移解析失败：{failureReason}");
                }
            }

            if (_config.TryGetSignalOffsets(out var offsets))
            {
                _offsets = offsets;
                Logger.Info(
                    "DMA",
                    $"使用配置中的偏移：hooked=0x{offsets.HookedOffset:X}, catch=0x{offsets.CatchCompletedOffset:X}, tension=0x{offsets.TensionOffset:X}。");
            }
            else if (TryResolveSignalOffsetsFromIl2CppInspectorPro(il2CppLayout, il2CppSelection, out offsets, out var signalSource))
            {
                _offsets = offsets;
                Logger.Info(
                    "DMA",
                    $"使用 Il2CppInspectorPro 导出的偏移：hooked=0x{offsets.HookedOffset:X}, catch=0x{offsets.CatchCompletedOffset:X}, tension=0x{offsets.TensionOffset:X}（{signalSource}）。");
            }
            else
            {
                if (_config.TryGetTensionOffset(out var configuredTensionOffset))
                {
                    _fallbackTensionOffset = configuredTensionOffset;
                }
                else if (TryResolveTensionOffsetFromIl2CppInspectorPro(il2CppLayout, il2CppSelection, out var resolvedTensionOffset, out var tensionSource))
                {
                    _fallbackTensionOffset = resolvedTensionOffset;
                    Logger.Info("DMA", $"已从 Il2CppInspectorPro 解析张力偏移 0x{_fallbackTensionOffset:X}（{tensionSource}）。");
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
            else if (TryResolvePositionOffsetsFromIl2CppInspectorPro(
                         il2CppLayout,
                         il2CppSelection,
                         out var il2CppPositionOffsets,
                         out var positionSource))
            {
                _positionOffsets = il2CppPositionOffsets;
                Logger.Info(
                    "DMA",
                    $"已从 Il2CppInspectorPro 启用位置控制偏移：fish=0x{il2CppPositionOffsets.FishPositionOffset:X}, barCenter=0x{il2CppPositionOffsets.BarCenterOffset:X}, barHeight=0x{il2CppPositionOffsets.BarHeightOffset:X}（{positionSource}）。");
            }
            else if (TryResolveBarRangeOffsetsFromIl2CppInspectorPro(
                         il2CppLayout,
                         il2CppSelection,
                         out var il2CppBarRangeOffsets,
                         out var barRangeSource))
            {
                _barRangeOffsets = il2CppBarRangeOffsets;
                Logger.Info(
                    "DMA",
                    $"已从 Il2CppInspectorPro 启用位置控制偏移：fish=0x{il2CppBarRangeOffsets.FishPositionOffset:X}, barTop=0x{il2CppBarRangeOffsets.BarTopOffset:X}, barBottom=0x{il2CppBarRangeOffsets.BarBottomOffset:X}（{barRangeSource}）。");
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
        if (!IsReady || _memory is null)
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
        _memory?.Dispose();
        _vmm?.Dispose();
    }

    private T ReadValue<T>(ulong address) where T : unmanaged
    {
        if (_memory is null)
        {
            throw new InvalidOperationException("DMA 进程尚未初始化。");
        }

        if (!_memory.TryReadBytes(address, Marshal.SizeOf<T>(), out var bytes))
        {
            throw new InvalidOperationException($"读取 0x{address:X} 处的 {typeof(T).Name} 失败。");
        }

        return MemoryMarshal.Read<T>(bytes);
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

    private IProcessMemory? ResolveProcessMemory(string? configuredProcessName)
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
                return new VmmProcessMemory(process);
            }
        }

        return FindProcessMemoryFromSnapshot(configuredProcessName);
    }

    private IProcessMemory? FindProcessMemoryFromSnapshot(string configuredProcessName)
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
            return new VmmProcessMemory(exactMatch);
        }

        var fuzzyMatch = processes.FirstOrDefault(process =>
            process.IsValid &&
            NormalizeProcessName(process.Name).Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase));
        return fuzzyMatch is null ? null : new VmmProcessMemory(fuzzyMatch);
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

    private static bool TryResolveSignalOffsetsFromIl2CppInspectorPro(
        ResolvedIl2CppInspectorLayout? layout,
        Il2CppInspectorFieldSelection? selection,
        out SignalOffsets offsets,
        out string sourceDescription)
    {
        offsets = default;
        sourceDescription = string.Empty;

        if (layout is null || selection is null)
        {
            return false;
        }

        var value = selection.Value;
        if (!TryResolveInstanceFieldOffset(layout, value.HookedFieldName, out var hookedOffset) ||
            !TryResolveInstanceFieldOffset(layout, value.CatchCompletedFieldName, out var catchCompletedOffset) ||
            !TryResolveInstanceFieldOffset(layout, value.TensionFieldName, out var tensionOffset))
        {
            return false;
        }

        offsets = new SignalOffsets(hookedOffset, catchCompletedOffset, tensionOffset);
        sourceDescription = Path.GetFileName(layout.SourcePath);
        return true;
    }

    private static bool TryResolveTensionOffsetFromIl2CppInspectorPro(
        ResolvedIl2CppInspectorLayout? layout,
        Il2CppInspectorFieldSelection? selection,
        out ulong tensionOffset,
        out string sourceDescription)
    {
        tensionOffset = 0;
        sourceDescription = string.Empty;

        if (layout is null || selection is null)
        {
            return false;
        }

        if (!TryResolveInstanceFieldOffset(layout, selection.Value.TensionFieldName, out tensionOffset))
        {
            return false;
        }

        sourceDescription = Path.GetFileName(layout.SourcePath);
        return true;
    }

    private static bool TryResolvePositionOffsetsFromIl2CppInspectorPro(
        ResolvedIl2CppInspectorLayout? layout,
        Il2CppInspectorFieldSelection? selection,
        out PositionOffsets offsets,
        out string sourceDescription)
    {
        offsets = default;
        sourceDescription = string.Empty;

        if (layout is null || selection is null)
        {
            return false;
        }

        var value = selection.Value;
        if (!TryResolveInstanceFieldOffset(layout, value.FishPositionFieldName, out var fishOffset) ||
            !TryResolveInstanceFieldOffset(layout, value.BarCenterFieldName, out var barCenterOffset) ||
            !TryResolveInstanceFieldOffset(layout, value.BarHeightFieldName, out var barHeightOffset))
        {
            return false;
        }

        offsets = new PositionOffsets(fishOffset, barCenterOffset, barHeightOffset);
        sourceDescription = Path.GetFileName(layout.SourcePath);
        return true;
    }

    private static bool TryResolveBarRangeOffsetsFromIl2CppInspectorPro(
        ResolvedIl2CppInspectorLayout? layout,
        Il2CppInspectorFieldSelection? selection,
        out BarRangeOffsets offsets,
        out string sourceDescription)
    {
        offsets = default;
        sourceDescription = string.Empty;

        if (layout is null || selection is null)
        {
            return false;
        }

        var value = selection.Value;
        if (!TryResolveInstanceFieldOffset(layout, value.FishPositionFieldName, out var fishOffset) ||
            !TryResolveInstanceFieldOffset(layout, value.BarTopFieldName, out var barTopOffset) ||
            !TryResolveInstanceFieldOffset(layout, value.BarBottomFieldName, out var barBottomOffset))
        {
            return false;
        }

        offsets = new BarRangeOffsets(fishOffset, barTopOffset, barBottomOffset);
        sourceDescription = Path.GetFileName(layout.SourcePath);
        return true;
    }

    private static bool TryResolveInstanceFieldOffset(
        ResolvedIl2CppInspectorLayout layout,
        string? configuredFieldName,
        out ulong offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(configuredFieldName))
        {
            return false;
        }

        if (!layout.TryGetField(configuredFieldName, out var field) || field.IsStatic)
        {
            return false;
        }

        offset = field.Offset;
        return true;
    }

    public ulong ResolveGameObjectManagerAddress()
    {
        if (!HasConnectedProcess || _memory is null)
        {
            return 0;
        }

        if (TryResolveConfiguredAddress(_config.GameObjectManagerAddress, out var configuredAddress, out _))
        {
            return configuredAddress;
        }

        var scanner = new UnityScanner(_memory, _config.GetUnityNativeLayout());
        return scanner.FindGameObjectManager(_config.GetGameObjectManagerPatternCandidates());
    }

    public IReadOnlyList<UnityObjectInfo> DumpUnityObjects(int limit)
    {
        if (!HasConnectedProcess || _memory is null)
        {
            return Array.Empty<UnityObjectInfo>();
        }

        var scanner = new UnityScanner(_memory, _config.GetUnityNativeLayout());
        var gameObjectManagerAddress = TryResolveConfiguredAddress(_config.GameObjectManagerAddress, out var configuredAddress, out _)
            ? configuredAddress
            : 0;
        Logger.Debug("DMA", $"对象转储使用的 GameObjectManager 地址 0x{gameObjectManagerAddress:X}。");
        return scanner.DumpObjects(limit, gameObjectManagerAddress, _config.GetGameObjectManagerPatternCandidates());
    }

    public IReadOnlyList<GameObjectManagerProbeResult> DumpGameObjectManagerCandidates(int limit, bool includeReplayData = false)
    {
        if (!HasConnectedProcess || _memory is null || limit <= 0)
        {
            return Array.Empty<GameObjectManagerProbeResult>();
        }

        var scanner = new UnityScanner(_memory, _config.GetUnityNativeLayout());
        return scanner.ProbeGameObjectManagerCandidates(_config.GetGameObjectManagerPatternCandidates(), limit, includeReplayData);
    }

    private bool TryResolveConfiguredAddress(string? value, out ulong address, out string sourceDescription)
    {
        address = 0;
        sourceDescription = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (TryParseAbsoluteAddress(trimmed, out address))
        {
            sourceDescription = "absolute";
            return true;
        }

        if (_memory is null)
        {
            return false;
        }

        var plusIndex = trimmed.IndexOf('+');
        if (plusIndex <= 0 || plusIndex >= trimmed.Length - 1)
        {
            return false;
        }

        var moduleName = trimmed[..plusIndex].Trim();
        var offsetText = trimmed[(plusIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(moduleName) ||
            string.IsNullOrWhiteSpace(offsetText) ||
            !TryParseAbsoluteAddress(offsetText, out var offset))
        {
            return false;
        }

        var moduleBase = _memory.GetModuleBase(moduleName);
        if (moduleBase == 0)
        {
            return false;
        }

        address = moduleBase + offset;
        sourceDescription = $"{moduleName}+0x{offset:X}";
        return true;
    }

    private static bool TryParseAbsoluteAddress(string value, out ulong address)
    {
        address = 0;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out address);
        }

        return ulong.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out address);
    }
}
