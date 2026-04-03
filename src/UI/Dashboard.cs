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
                new Layout("Right").Ratio(2));
    }

    public static void Update(Layout layout, FishingBot bot, AppConfig config)
    {
        var statusTable = new Table().Border(TableBorder.Rounded).Expand();
        statusTable.AddColumn("[cyan]项目[/]");
        statusTable.AddColumn("[cyan]数值[/]");
        statusTable.AddRow("输入方式", $"[green]{GetInputTypeText(config.Input.Type)}[/]");
        statusTable.AddRow("连接地址", $"[yellow]{GetInputEndpoint(config)}[/]");
        statusTable.AddRow("信号来源", $"[green]{GetSignalSourceText(config.SignalSource.Type)}[/]");
        statusTable.AddRow("机器人状态", $"[bold yellow]{GetStateText(bot.State)}[/]");
        statusTable.AddRow("暂停阈值", $"[grey]{config.Bot.ReelTensionPauseThreshold:P0}[/]");
        statusTable.AddRow("恢复阈值", $"[grey]{config.Bot.ReelTensionResumeThreshold:P0}[/]");

        layout["Left"].Update(
            new Panel(statusTable)
                .Header("[bold magenta] 系统状态 [/]")
                .BorderColor(Color.Magenta));

        var tensionVal = Math.Clamp(bot.LastContext.Tension, 0f, 1f) * 100;
        var tensionColor = tensionVal > 80 ? Color.Red : tensionVal > 50 ? Color.Yellow : Color.Green;

        var bar = new BarChart()
            .Width(60)
            .Label($"[bold]当前张力: {tensionVal:F1}%[/]")
            .AddItem("张力", Math.Round(tensionVal), tensionColor);

        var hookStatus = bot.LastContext.IsHooked
            ? "[bold red]鱼儿已咬钩[/]"
            : "[grey]等待鱼儿上钩...[/]";

        layout["Right"].Update(
            new Panel(
                new Rows(
                    new Padder(new Markup(hookStatus).Centered(), new Padding(0, 1)),
                    bar))
                .Header("[bold cyan] 实时监控 [/]")
                .BorderColor(Color.Cyan1));
    }

    public static void Render(FishingBot bot, AppConfig config)
    {
        AnsiConsole.MarkupLine(
            $"[cyan]状态：[/] {GetStateText(bot.State)} | [cyan]张力：[/] {Math.Clamp(bot.LastContext.Tension, 0f, 1f):P1} | [cyan]输入：[/] {GetInputTypeText(config.Input.Type)} | [cyan]信号：[/] {GetSignalSourceText(config.SignalSource.Type)}");
    }

    private static string GetInputEndpoint(AppConfig config)
    {
        if (string.Equals(config.Input.Type, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            var port = string.Equals(config.Input.ComPort, "Auto", StringComparison.OrdinalIgnoreCase) ? "自动" : config.Input.ComPort;
            return $"{port} @ {config.Input.BaudRate}";
        }

        if (string.Equals(config.Input.Type, "Net", StringComparison.OrdinalIgnoreCase))
        {
            return $"{config.Input.NetIp}:{config.Input.NetPort}";
        }

        return GetInputTypeText(config.Input.Type);
    }

    private static string GetInputTypeText(string? type)
    {
        if (string.Equals(type, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            return "串口 KMBOX";
        }

        if (string.Equals(type, "Net", StringComparison.OrdinalIgnoreCase))
        {
            return "网络 KMBOX";
        }

        if (string.Equals(type, "Console", StringComparison.OrdinalIgnoreCase))
        {
            return "控制台";
        }

        return "模拟";
    }

    private static string GetSignalSourceText(string? type) =>
        string.Equals(type, "Dma", StringComparison.OrdinalIgnoreCase) ? "DMA" : "模拟";

    private static string GetStateText(FishState state) => state switch
    {
        FishState.Idle => "待机",
        FishState.Casting => "抛竿中",
        FishState.Waiting => "等待上钩",
        FishState.Hooked => "咬钩确认",
        FishState.Reeling => "收线中",
        FishState.Cooldown => "冷却中",
        _ => state.ToString(),
    };
}
