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
        statusTable.AddColumn("[cyan]Setting[/]");
        statusTable.AddColumn("[cyan]Value[/]");
        statusTable.AddRow("Input", $"[green]{config.Input.Type}[/]");
        statusTable.AddRow("Endpoint", $"[yellow]{GetInputEndpoint(config)}[/]");
        statusTable.AddRow("Signal", $"[green]{config.SignalSource.Type}[/]");
        statusTable.AddRow("Bot state", $"[bold yellow]{bot.State}[/]");
        statusTable.AddRow("Pause threshold", $"[grey]{config.Bot.ReelTensionPauseThreshold:P0}[/]");
        statusTable.AddRow("Resume threshold", $"[grey]{config.Bot.ReelTensionResumeThreshold:P0}[/]");

        layout["Left"].Update(
            new Panel(statusTable)
                .Header("[bold magenta] System [/]")
                .BorderColor(Color.Magenta));

        var tensionVal = Math.Clamp(bot.LastContext.Tension, 0f, 1f) * 100;
        var tensionColor = tensionVal > 80 ? Color.Red : tensionVal > 50 ? Color.Yellow : Color.Green;

        var bar = new BarChart()
            .Width(60)
            .Label($"[bold]Live tension: {tensionVal:F1}%[/]")
            .AddItem("Tension", Math.Round(tensionVal), tensionColor);

        var hookStatus = bot.LastContext.IsHooked
            ? "[bold red]Fish hooked[/]"
            : "[grey]Waiting for bite...[/]";

        layout["Right"].Update(
            new Panel(
                new Rows(
                    new Padder(new Markup(hookStatus).Centered(), new Padding(0, 1)),
                    bar))
                .Header("[bold cyan] Live Monitor [/]")
                .BorderColor(Color.Cyan1));
    }

    public static void Render(FishingBot bot, AppConfig config)
    {
        AnsiConsole.MarkupLine(
            $"[cyan]State:[/] {bot.State} | [cyan]Tension:[/] {Math.Clamp(bot.LastContext.Tension, 0f, 1f):P1} | [cyan]Input:[/] {config.Input.Type} | [cyan]Signal:[/] {config.SignalSource.Type}");
    }

    private static string GetInputEndpoint(AppConfig config)
    {
        if (string.Equals(config.Input.Type, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            return $"{config.Input.ComPort} @ {config.Input.BaudRate}";
        }

        if (string.Equals(config.Input.Type, "Net", StringComparison.OrdinalIgnoreCase))
        {
            return $"{config.Input.NetIp}:{config.Input.NetPort}";
        }

        return config.Input.Type;
    }
}
