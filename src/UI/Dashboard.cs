using Spectre.Console;
using VrcDmaFish.Core;
using VrcDmaFish.Models;

namespace VrcDmaFish.UI;

public static class Dashboard
{
    public static Layout CreateLayout()
    {
        return new Layout("Root")
            .SplitColumns(
                new Layout("Left").Ratio(1),
                new Layout("Right").Ratio(2)
            );
    }

    public static void Update(Layout layout, FishingBot bot, AppConfig config)
    {
        // --- 左侧：配置与状态面板 ---
        var statusTable = new Table().Border(TableBorder.Rounded).Expand();
        statusTable.AddColumn("[cyan]参数[/]");
        statusTable.AddColumn("[cyan]状态[/]");
        statusTable.AddRow("控制器", $"[green]{config.Input.Type}[/]");
        statusTable.AddRow("输入口", $"[yellow]{(config.Input.Type == "Serial" ? config.Input.ComPort : config.Input.NetIp)}[/]");
        statusTable.AddRow("机器人状态", $"[bold yellow]{bot.State}[/]");
        statusTable.AddRow("张力阈值", $"[grey]{config.Bot.ReelTensionPauseThreshold:P0}[/]");

        layout["Left"].Update(
            new Panel(statusTable)
                .Header("[bold magenta] 系统信息 [/]")
                .BorderColor(Color.Magenta)
        );

        // --- 右侧：实时监控面板 ---
        var tensionVal = bot.LastContext.Tension * 100;
        var tensionColor = tensionVal > 80 ? Color.Red : (tensionVal > 50 ? Color.Yellow : Color.Green);
        
        // 动态进度条
        var bar = new BarChart()
            .Width(60)
            .Label($"[bold]实时张力: {tensionVal:F1}%[/]")
            .AddItem("Tension", Math.Round(tensionVal), tensionColor);

        var hookStatus = bot.LastContext.IsHooked 
            ? "[blink bold red]!!! FISH HOOKED !!![/]" 
            : "[grey]Waiting for bite...[/]";

        layout["Right"].Update(
            new Panel(
                new Rows(
                    new Padder(new Text(hookStatus).Centered(), new Padding(0, 1)),
                    bar
                )
            ).Header("[bold cyan] 实时监控 [/]")
            .BorderColor(Color.Cyan1)
        );
    }
}
