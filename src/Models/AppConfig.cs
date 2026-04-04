using System.Globalization;
using System.Text.Json;

namespace VrcDmaFish.Models;

public sealed class AppConfig
{
    public int TickIntervalMs { get; set; } = 60;
    public LoggingConfig Logging { get; set; } = new();
    public InputConfig Input { get; set; } = new();
    public SignalSourceConfig SignalSource { get; set; } = new();
    public BotConfig Bot { get; set; } = new();

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        var jsonOptions = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), jsonOptions) ?? new AppConfig();
        config.Logging ??= new LoggingConfig();
        config.Input ??= new InputConfig();
        config.SignalSource ??= new SignalSourceConfig();
        config.SignalSource.Il2CppInspectorPro ??= new Il2CppInspectorProConfig();
        config.Bot ??= new BotConfig();
        return config;
    }

    public IReadOnlyList<string> Normalize()
    {
        var warnings = new List<string>();

        if (TickIntervalMs < 10)
        {
            warnings.Add($"TickIntervalMs={TickIntervalMs} 过小，已调整为 10ms。");
            TickIntervalMs = 10;
        }

        if (string.IsNullOrWhiteSpace(Logging.Level))
        {
            Logging.Level = "Info";
        }
        else if (!LogLevelParser.TryParse(Logging.Level, out var consoleLevel))
        {
            warnings.Add($"Logging.Level='{Logging.Level}' 无效，已调整为 Info。");
            Logging.Level = "Info";
        }
        else
        {
            Logging.Level = LogLevelParser.ToDisplayName(consoleLevel);
        }

        if (string.IsNullOrWhiteSpace(Logging.FileLevel))
        {
            Logging.FileLevel = Logging.Level;
        }
        else if (!LogLevelParser.TryParse(Logging.FileLevel, out var fileLevel))
        {
            warnings.Add($"Logging.FileLevel='{Logging.FileLevel}' 无效，已调整为 {Logging.Level}。");
            Logging.FileLevel = Logging.Level;
        }
        else
        {
            Logging.FileLevel = LogLevelParser.ToDisplayName(fileLevel);
        }

        if (!string.IsNullOrWhiteSpace(Logging.FilePath))
        {
            Logging.FilePath = Logging.FilePath.Trim();
        }

        if (Bot.CastDurationMs < 0)
        {
            warnings.Add($"CastDurationMs={Bot.CastDurationMs} 无效，已调整为 0。");
            Bot.CastDurationMs = 0;
        }

        if (Bot.CooldownMs < 0)
        {
            warnings.Add($"CooldownMs={Bot.CooldownMs} 无效，已调整为 0。");
            Bot.CooldownMs = 0;
        }

        if (Bot.HookClickMs < 0)
        {
            warnings.Add($"HookClickMs={Bot.HookClickMs} 无效，已调整为 0。");
            Bot.HookClickMs = 0;
        }

        if (Bot.ReelPulseMs < 0)
        {
            warnings.Add($"ReelPulseMs={Bot.ReelPulseMs} 无效，已调整为 0。");
            Bot.ReelPulseMs = 0;
        }

        if (Bot.ReelHoldMinMs < 0)
        {
            warnings.Add($"ReelHoldMinMs={Bot.ReelHoldMinMs} 无效，已调整为 0。");
            Bot.ReelHoldMinMs = 0;
        }

        if (Bot.ReelHoldMaxMs < 0)
        {
            warnings.Add($"ReelHoldMaxMs={Bot.ReelHoldMaxMs} 无效，已调整为 0。");
            Bot.ReelHoldMaxMs = 0;
        }

        if (Bot.ReelHoldGainMs < 0)
        {
            warnings.Add($"ReelHoldGainMs={Bot.ReelHoldGainMs} 无效，已调整为 0。");
            Bot.ReelHoldGainMs = 0;
        }

        if (Bot.ReelVelocityDampingMs < 0)
        {
            warnings.Add($"ReelVelocityDampingMs={Bot.ReelVelocityDampingMs} 无效，已调整为 0。");
            Bot.ReelVelocityDampingMs = 0;
        }

        if (Bot.ReelRestMs < 0)
        {
            warnings.Add($"ReelRestMs={Bot.ReelRestMs} 无效，已调整为 0。");
            Bot.ReelRestMs = 0;
        }

        if (Bot.PositionBaseHoldMs < 0)
        {
            warnings.Add($"PositionBaseHoldMs={Bot.PositionBaseHoldMs} 无效，已调整为 0。");
            Bot.PositionBaseHoldMs = 0;
        }

        if (Bot.PositionHoldGainMs < 0)
        {
            warnings.Add($"PositionHoldGainMs={Bot.PositionHoldGainMs} 无效，已调整为 0。");
            Bot.PositionHoldGainMs = 0;
        }

        if (Bot.PositionVelocityDampingMs < 0)
        {
            warnings.Add($"PositionVelocityDampingMs={Bot.PositionVelocityDampingMs} 无效，已调整为 0。");
            Bot.PositionVelocityDampingMs = 0;
        }

        Bot.PositionVelocitySmooth = Math.Clamp(Bot.PositionVelocitySmooth, 0d, 0.95d);
        Bot.PositionDeadZoneRatio = Math.Clamp(Bot.PositionDeadZoneRatio, 0f, 1f);

        if (Input.BaudRate <= 0)
        {
            warnings.Add($"BaudRate={Input.BaudRate} 无效，已调整为 115200。");
            Input.BaudRate = 115200;
        }

        if (Input.NetPort <= 0)
        {
            warnings.Add($"NetPort={Input.NetPort} 无效，已调整为 8006。");
            Input.NetPort = 8006;
        }

        Bot.ReelTensionPauseThreshold = Math.Clamp(Bot.ReelTensionPauseThreshold, 0f, 1f);
        Bot.ReelTensionResumeThreshold = Math.Clamp(Bot.ReelTensionResumeThreshold, 0f, 1f);

        if (Bot.ReelTensionPauseThreshold < Bot.ReelTensionResumeThreshold)
        {
            warnings.Add(
                $"ReelTensionPauseThreshold={Bot.ReelTensionPauseThreshold} 必须大于等于 ReelTensionResumeThreshold={Bot.ReelTensionResumeThreshold}，已自动对齐。");
            Bot.ReelTensionPauseThreshold = Bot.ReelTensionResumeThreshold;
        }

        if (Bot.ReelHoldMaxMs < Bot.ReelHoldMinMs)
        {
            warnings.Add(
                $"ReelHoldMaxMs={Bot.ReelHoldMaxMs} 不能小于 ReelHoldMinMs={Bot.ReelHoldMinMs}，已自动对齐。");
            Bot.ReelHoldMaxMs = Bot.ReelHoldMinMs;
        }

        if (string.IsNullOrWhiteSpace(Input.Type))
        {
            Input.Type = "Mock";
        }

        if (string.IsNullOrWhiteSpace(Input.ComPort))
        {
            Input.ComPort = "Auto";
        }

        if (string.IsNullOrWhiteSpace(Input.NetIp))
        {
            Input.NetIp = "192.168.2.188";
        }

        if (string.IsNullOrWhiteSpace(SignalSource.Type))
        {
            SignalSource.Type = string.Equals(Input.Type, "Mock", StringComparison.OrdinalIgnoreCase)
                ? "Mock"
                : "Dma";
        }

        if (string.IsNullOrWhiteSpace(SignalSource.ProcessName))
        {
            SignalSource.ProcessName = "VRChat";
        }

        if (string.IsNullOrWhiteSpace(SignalSource.TargetObjectName))
        {
            SignalSource.TargetObjectName = "FishingLogic";
        }

        SignalSource.Il2CppInspectorPro ??= new Il2CppInspectorProConfig();
        if (!string.IsNullOrWhiteSpace(SignalSource.Il2CppInspectorPro.CSharpOutputPath))
        {
            SignalSource.Il2CppInspectorPro.CSharpOutputPath = SignalSource.Il2CppInspectorPro.CSharpOutputPath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(SignalSource.Il2CppInspectorPro.TargetTypeName))
        {
            SignalSource.Il2CppInspectorPro.TargetTypeName = SignalSource.Il2CppInspectorPro.TargetTypeName.Trim();
        }

        return warnings;
    }
}

public sealed class InputConfig
{
    public string Type { get; set; } = "Mock";
    public string ComPort { get; set; } = "Auto";
    public int BaudRate { get; set; } = 115200;
    public string NetIp { get; set; } = "192.168.2.188";
    public int NetPort { get; set; } = 8006;
}

public sealed class SignalSourceConfig
{
    public string Type { get; set; } = "Mock";
    public string ProcessName { get; set; } = "VRChat";
    public string TargetObjectName { get; set; } = "FishingLogic";
    public string? TargetKlassAddress { get; set; }
    public string? GameObjectManagerPattern { get; set; }
    public string[]? GameObjectManagerPatterns { get; set; }
    public string? GameObjectManagerAddress { get; set; }
    public string? TargetObjectAddress { get; set; }
    public string? HookedOffset { get; set; }
    public string? CatchCompletedOffset { get; set; }
    public string? TensionOffset { get; set; }
    public string? FishPositionOffset { get; set; }
    public string? BarCenterOffset { get; set; }
    public string? BarHeightOffset { get; set; }
    public string? BarTopOffset { get; set; }
    public string? BarBottomOffset { get; set; }
    public string? GameObjectManagerActiveNodesOffset { get; set; }
    public string? ObjectNodePreviousOffset { get; set; }
    public string? ObjectNodeNextOffset { get; set; }
    public string? ObjectNodeGameObjectOffset { get; set; }
    public string? GameObjectComponentArrayOffset { get; set; }
    public string? GameObjectComponentCountOffset { get; set; }
    public string? GameObjectNamePointerOffset { get; set; }
    public string? ComponentArrayElementStride { get; set; }
    public string? ComponentArrayElementTypeInfoOffset { get; set; }
    public string? ComponentArrayElementComponentPointerOffset { get; set; }
    public string? ComponentKlassPointerOffset { get; set; }
    public string? ComponentGameObjectOffset { get; set; }
    public string? Il2CppClassNamePointerOffset { get; set; }
    public string? Il2CppClassNamespacePointerOffset { get; set; }
    public string? Il2CppClassParentPointerOffset { get; set; }
    public int? MaxComponentCount { get; set; }
    public int? MaxStringLength { get; set; }
    public int? MaxClassParentDepth { get; set; }
    public Il2CppInspectorProConfig? Il2CppInspectorPro { get; set; } = new();

    public bool TryGetGameObjectManagerAddress(out ulong address) => TryParseAddress(GameObjectManagerAddress, out address);

    public bool TryGetTargetObjectAddress(out ulong address) => TryParseAddress(TargetObjectAddress, out address);

    public bool TryGetTargetKlassAddress(out ulong address) => TryParseAddress(TargetKlassAddress, out address);

    public IReadOnlyList<string> GetGameObjectManagerPatternCandidates()
    {
        var patterns = new List<string>();
        AddPattern(patterns, GameObjectManagerPattern);

        if (GameObjectManagerPatterns is not null)
        {
            foreach (var pattern in GameObjectManagerPatterns)
            {
                AddPattern(patterns, pattern);
            }
        }

        return patterns;
    }

    public bool TryGetSignalOffsets(out SignalOffsets offsets)
    {
        offsets = default;

        if (!TryParseAddress(HookedOffset, out var hookedOffset) ||
            !TryParseAddress(CatchCompletedOffset, out var catchCompletedOffset) ||
            !TryParseAddress(TensionOffset, out var tensionOffset))
        {
            return false;
        }

        offsets = new SignalOffsets(hookedOffset, catchCompletedOffset, tensionOffset);
        return true;
    }

    public bool TryGetTensionOffset(out ulong offset) => TryParseAddress(TensionOffset, out offset);

    public bool TryGetPositionOffsets(out PositionOffsets offsets)
    {
        offsets = default;

        if (!TryParseAddress(FishPositionOffset, out var fishPositionOffset) ||
            !TryParseAddress(BarCenterOffset, out var barCenterOffset) ||
            !TryParseAddress(BarHeightOffset, out var barHeightOffset))
        {
            return false;
        }

        offsets = new PositionOffsets(fishPositionOffset, barCenterOffset, barHeightOffset);
        return true;
    }

    public bool TryGetBarRangeOffsets(out BarRangeOffsets offsets)
    {
        offsets = default;

        if (!TryParseAddress(FishPositionOffset, out var fishPositionOffset) ||
            !TryParseAddress(BarTopOffset, out var barTopOffset) ||
            !TryParseAddress(BarBottomOffset, out var barBottomOffset))
        {
            return false;
        }

        offsets = new BarRangeOffsets(fishPositionOffset, barTopOffset, barBottomOffset);
        return true;
    }

    public bool TryGetIl2CppInspectorProSelection(out Il2CppInspectorFieldSelection selection)
    {
        selection = default;

        if (Il2CppInspectorPro is null ||
            string.IsNullOrWhiteSpace(Il2CppInspectorPro.CSharpOutputPath) ||
            string.IsNullOrWhiteSpace(Il2CppInspectorPro.TargetTypeName))
        {
            return false;
        }

        selection = new Il2CppInspectorFieldSelection(
            Il2CppInspectorPro.CSharpOutputPath.Trim(),
            Il2CppInspectorPro.TargetTypeName.Trim(),
            NormalizeFieldName(Il2CppInspectorPro.HookedFieldName),
            NormalizeFieldName(Il2CppInspectorPro.CatchCompletedFieldName),
            NormalizeFieldName(Il2CppInspectorPro.TensionFieldName),
            NormalizeFieldName(Il2CppInspectorPro.FishPositionFieldName),
            NormalizeFieldName(Il2CppInspectorPro.BarCenterFieldName),
            NormalizeFieldName(Il2CppInspectorPro.BarHeightFieldName),
            NormalizeFieldName(Il2CppInspectorPro.BarTopFieldName),
            NormalizeFieldName(Il2CppInspectorPro.BarBottomFieldName));
        return true;
    }

    public UnityNativeLayout GetUnityNativeLayout()
    {
        var layout = UnityNativeLayout.Default;

        if (TryParseAddress(GameObjectManagerActiveNodesOffset, out var activeNodesOffset))
        {
            layout = layout with { GameObjectManagerActiveNodesOffset = activeNodesOffset };
        }

        if (TryParseAddress(ObjectNodePreviousOffset, out var previousOffset))
        {
            layout = layout with { ObjectNodePreviousOffset = previousOffset };
        }

        if (TryParseAddress(ObjectNodeNextOffset, out var nextOffset))
        {
            layout = layout with { ObjectNodeNextOffset = nextOffset };
        }

        if (TryParseAddress(ObjectNodeGameObjectOffset, out var gameObjectOffset))
        {
            layout = layout with { ObjectNodeGameObjectOffset = gameObjectOffset };
        }

        if (TryParseAddress(GameObjectNamePointerOffset, out var namePointerOffset))
        {
            layout = layout with { GameObjectNamePointerOffset = namePointerOffset };
        }

        if (TryParseAddress(GameObjectComponentArrayOffset, out var componentArrayOffset))
        {
            layout = layout with { GameObjectComponentArrayOffset = componentArrayOffset };
        }

        if (TryParseAddress(GameObjectComponentCountOffset, out var componentCountOffset))
        {
            layout = layout with { GameObjectComponentCountOffset = componentCountOffset };
        }

        if (TryParseAddress(ComponentArrayElementStride, out var componentArrayElementStride))
        {
            layout = layout with { ComponentArrayElementStride = componentArrayElementStride };
        }

        if (TryParseAddress(ComponentArrayElementTypeInfoOffset, out var componentArrayTypeInfoOffset))
        {
            layout = layout with { ComponentArrayElementTypeInfoOffset = componentArrayTypeInfoOffset };
        }

        if (TryParseAddress(ComponentArrayElementComponentPointerOffset, out var componentArrayPointerOffset))
        {
            layout = layout with { ComponentArrayElementComponentPointerOffset = componentArrayPointerOffset };
        }

        if (TryParseAddress(ComponentKlassPointerOffset, out var componentKlassPointerOffset))
        {
            layout = layout with { ComponentKlassPointerOffset = componentKlassPointerOffset };
        }

        if (TryParseAddress(ComponentGameObjectOffset, out var componentGameObjectOffset))
        {
            layout = layout with { ComponentGameObjectOffset = componentGameObjectOffset };
        }

        if (TryParseAddress(Il2CppClassNamePointerOffset, out var il2CppClassNamePointerOffset))
        {
            layout = layout with { Il2CppClassNamePointerOffset = il2CppClassNamePointerOffset };
        }

        if (TryParseAddress(Il2CppClassNamespacePointerOffset, out var il2CppClassNamespacePointerOffset))
        {
            layout = layout with { Il2CppClassNamespacePointerOffset = il2CppClassNamespacePointerOffset };
        }

        if (TryParseAddress(Il2CppClassParentPointerOffset, out var il2CppClassParentPointerOffset))
        {
            layout = layout with { Il2CppClassParentPointerOffset = il2CppClassParentPointerOffset };
        }

        if (MaxComponentCount is > 0 and < 100000)
        {
            layout = layout with { MaxComponentCount = MaxComponentCount.Value };
        }

        if (MaxStringLength is > 0 and < 4096)
        {
            layout = layout with { MaxStringLength = MaxStringLength.Value };
        }

        if (MaxClassParentDepth is > 0 and < 256)
        {
            layout = layout with { MaxClassParentDepth = MaxClassParentDepth.Value };
        }

        return layout;
    }

    private static bool TryParseAddress(string? text, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void AddPattern(List<string> patterns, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        var trimmed = pattern.Trim();
        if (!patterns.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            patterns.Add(trimmed);
        }
    }

    private static string? NormalizeFieldName(string? fieldName)
    {
        return string.IsNullOrWhiteSpace(fieldName)
            ? null
            : fieldName.Trim();
    }
}

public readonly record struct SignalOffsets(ulong HookedOffset, ulong CatchCompletedOffset, ulong TensionOffset);
public readonly record struct PositionOffsets(ulong FishPositionOffset, ulong BarCenterOffset, ulong BarHeightOffset);
public readonly record struct BarRangeOffsets(ulong FishPositionOffset, ulong BarTopOffset, ulong BarBottomOffset);
public readonly record struct UnityNativeLayout(
    ulong GameObjectManagerActiveNodesOffset,
    ulong ObjectNodePreviousOffset,
    ulong ObjectNodeNextOffset,
    ulong ObjectNodeGameObjectOffset,
    ulong GameObjectComponentArrayOffset,
    ulong GameObjectComponentCountOffset,
    ulong GameObjectNamePointerOffset,
    ulong ComponentArrayElementStride,
    ulong ComponentArrayElementTypeInfoOffset,
    ulong ComponentArrayElementComponentPointerOffset,
    ulong ComponentKlassPointerOffset,
    ulong ComponentGameObjectOffset,
    ulong Il2CppClassNamePointerOffset,
    ulong Il2CppClassNamespacePointerOffset,
    ulong Il2CppClassParentPointerOffset,
    int MaxComponentCount,
    int MaxStringLength,
    int MaxClassParentDepth)
{
    // Unity 2022.3.x native GameObjectManager / ObjectNode / GameObject defaults.
    public static UnityNativeLayout Default { get; } = new(0x28, 0x00, 0x08, 0x10, 0x30, 0x40, 0x60, 0x10, 0x00, 0x08, 0x00, 0x10, 0x10, 0x18, 0x48, 1000, 64, 8);
}

public readonly record struct Il2CppInspectorFieldSelection(
    string CSharpOutputPath,
    string TargetTypeName,
    string? HookedFieldName,
    string? CatchCompletedFieldName,
    string? TensionFieldName,
    string? FishPositionFieldName,
    string? BarCenterFieldName,
    string? BarHeightFieldName,
    string? BarTopFieldName,
    string? BarBottomFieldName);

public sealed class Il2CppInspectorProConfig
{
    public string? CSharpOutputPath { get; set; }
    public string? TargetTypeName { get; set; }
    public string? HookedFieldName { get; set; }
    public string? CatchCompletedFieldName { get; set; }
    public string? TensionFieldName { get; set; }
    public string? FishPositionFieldName { get; set; }
    public string? BarCenterFieldName { get; set; }
    public string? BarHeightFieldName { get; set; }
    public string? BarTopFieldName { get; set; }
    public string? BarBottomFieldName { get; set; }
}
