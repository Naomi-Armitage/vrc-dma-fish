using System.Globalization;
using System.Text.Json;

namespace VrcDmaFish.Models;

public sealed class AppConfig
{
    public int TickIntervalMs { get; set; } = 100;
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
        config.Input ??= new InputConfig();
        config.SignalSource ??= new SignalSourceConfig();
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
    public string? TargetObjectAddress { get; set; }
    public string? HookedOffset { get; set; }
    public string? CatchCompletedOffset { get; set; }
    public string? TensionOffset { get; set; }

    public bool TryGetTargetObjectAddress(out ulong address) => TryParseAddress(TargetObjectAddress, out address);

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
}

public readonly record struct SignalOffsets(ulong HookedOffset, ulong CatchCompletedOffset, ulong TensionOffset);
