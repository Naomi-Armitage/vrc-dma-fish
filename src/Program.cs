using System.Text.Json;
using VrcDmaFish.Core;
using VrcDmaFish.Inputs;
using VrcDmaFish.Models;
using VrcDmaFish.Providers;
using VrcDmaFish.UI;
namespace VrcDmaFish;
public static class Program {
    public static void Main() {
        var cfg = new AppConfig();
        var bot = new FishingBot(new MockFishSignalSource(), new ConsoleInputController(), cfg.Bot);
        while (true) { bot.Tick(); Dashboard.Render(bot); Thread.Sleep(cfg.TickIntervalMs); }
    }
}
