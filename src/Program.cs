using System.Text.Json;
using Spectre.Console;
using VrcDmaFish.Core;
using VrcDmaFish.Inputs;
using VrcDmaFish.Models;
using VrcDmaFish.Providers;
using VrcDmaFish.UI;

namespace VrcDmaFish;

public static class Program
{
    public static void Main()
    {
        var config = LoadConfig();
        config = ConfigWizard.Run(config); // 允许在 UI 中修改配置并保存

        // 真正的输入控制器选择
        IInputController input = config.Input.Type switch {
            "Serial" => new KmboxInputController(config.Input.ComPort, config.Input.BaudRate),
            "Net" => new KmboxNetInputController(config.Input.NetIp, config.Input.NetPort),
            _ => new MockInputController()
        };

        // 真正的 DMA 提供者
        IFishSignalSource source = (config.Input.Type != "Mock") 
            ? new DmaProvider("VRChat") 
            : new MockFishSignalSource();

        var bot = new FishingBot(source, input, config.Bot);
        var layout = Dashboard.CreateLayout();

        AnsiConsole.Live(layout).Start(ctx => {
            while (true) {
                bot.Tick();
                Dashboard.Update(layout, bot, config);
                ctx.Refresh();
                Thread.Sleep(config.TickIntervalMs);
            }
        });
    }

    private static AppConfig LoadConfig() {
        if (!File.Exists("appsettings.json")) return new AppConfig();
        var json = File.ReadAllText("appsettings.json");
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }
}
