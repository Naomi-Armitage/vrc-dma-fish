using System.Text.Json;

namespace VrcDmaFish;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Ciallo~ VrcDmaFish safe prototype starting");

        var config = LoadConfig();
        Logger.Info("APP", $"TickIntervalMs={config.TickIntervalMs}");

        IFishSignalSource signalSource = new MockFishSignalSource();
        IInputController input = new ConsoleInputController();
        var bot = new FishingBot(signalSource, input, config.Bot);

        while (true)
        {
            bot.Tick();
            Thread.Sleep(config.TickIntervalMs);
        }
    }

    private static AppConfig LoadConfig()
    {
        const string path = "appsettings.json";
        if (!File.Exists(path))
        {
            var cfg = new AppConfig();
            SaveDefault(path, cfg);
            return cfg;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppConfig();
    }

    private static void SaveDefault(string path, AppConfig cfg)
    {
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }
}
