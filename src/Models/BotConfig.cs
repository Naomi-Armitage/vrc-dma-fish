namespace VrcDmaFish.Models;
public sealed class BotConfig {
    public int CastDurationMs { get; set; } = 1200;
    public int CooldownMs { get; set; } = 1500;
    public float ReelTensionPauseThreshold { get; set; } = 0.8f;
    public float ReelTensionResumeThreshold { get; set; } = 0.55f;
    public int HookClickMs { get; set; } = 60;
    public int ReelPulseMs { get; set; } = 80;
    public int ReelHoldMinMs { get; set; } = 5;
    public int ReelHoldMaxMs { get; set; } = 100;
    public int ReelHoldGainMs { get; set; } = 60;
    public int ReelVelocityDampingMs { get; set; } = 40;
    public int ReelRestMs { get; set; } = 120;
}
