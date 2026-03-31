namespace VrcDmaFish;

public sealed class BotConfig
{
    public int CastDurationMs { get; set; } = 1200;
    public int CooldownMs { get; set; } = 1500;
    public double ReelTensionPauseThreshold { get; set; } = 0.80;
    public double ReelTensionResumeThreshold { get; set; } = 0.55;
    public int ReelPulseMs { get; set; } = 80;
    public int ReelRestMs { get; set; } = 120;
}
