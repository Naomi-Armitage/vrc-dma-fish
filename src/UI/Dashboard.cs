using Spectre.Console;
using VrcDmaFish.Core;
namespace VrcDmaFish.UI;
public static class Dashboard {
    public static void Render(FishingBot bot) {
        // 简化版 UI 确保编译通过
        AnsiConsole.MarkupLine($"[cyan]Status:[/] {bot.State} | [cyan]Tension:[/] {bot.LastContext.Tension:P1}");
    }
}
