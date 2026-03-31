using System.Text.Json;
using VrcDmaFish.Inputs;

namespace VrcDmaFish;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Ciallo~ VrcDmaFish multi-input version starting! (∠・ω< )⌒★");

        var config = LoadConfig();
        
        IInputController input = config.Input.Type.ToLower() switch
        {
            "serial" => new SerialKmboxController(config.Input.ComPort),
            "net" => new NetKmboxController(config.Input.NetIp, config.Input.NetPort),
            "mock" => new ConsoleInputController(),
            _ => throw new ArgumentException("Unknown input type喵!")
        };

        IFishSignalSource signalSource = new MockFishSignalSource();
        var bot = new FishingBot(signalSource, input, config.Bot);

        Logger.Info("APP", $"Controller [{config.Input.Type}] loaded. System ready.");

        while (true)
        {
            bot.Tick();
            Thread.Sleep(config.TickIntervalMs);
        }
    }

    private static AppConfig LoadConfig()
    {
        const string path = "appsettings.json";
        if (!File.Exists(path)) return new AppConfig();
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
    }
}
