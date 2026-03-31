namespace VrcDmaFish;

public sealed class AppConfig
{
    public int TickIntervalMs { get; set; } = 100;
    public BotConfig Bot { get; set; } = new();
}
