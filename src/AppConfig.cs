namespace VrcDmaFish;

public sealed class AppConfig
{
    public int TickIntervalMs { get; set; } = 100;
    public InputConfig Input { get; set; } = new();
    public BotConfig Bot { get; set; } = new();
}

public sealed class InputConfig
{
    public string Type { get; set; } = "Mock";
    public string ComPort { get; set; } = "Auto"; // 默认 Auto 自动识别喵
    public int BaudRate { get; set; } = 115200;  // 默认波特率喵
    public string NetIp { get; set; } = "192.168.2.188";
    public int NetPort { get; set; } = 8006;
}
