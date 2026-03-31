namespace VrcDmaFish.Models;
public sealed class BotConfig {
    public int CastDurationMs { get; set; } = 1200;
    public int CooldownMs { get; set; } = 1500;
    public float ReelTensionPauseThreshold { get; set; } = 0.8f;
    public float ReelTensionResumeThreshold { get; set; } = 0.55f;
    public int ReelPulseMs { get; set; } = 80;
    public int ReelRestMs { get; set; } = 120;
}
