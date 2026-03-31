using System.IO.Ports;
using System.Text.Json;
using Spectre.Console;
using VrcDmaFish.Models;

namespace VrcDmaFish.UI;

public static class ConfigWizard
{
    public static AppConfig Run(AppConfig current, string configPath)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("VrcDmaFish").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[bold cyan]Interactive setup[/]\n");

        if (!AnsiConsole.Confirm("Edit the current configuration?", false))
        {
            return current;
        }

        var previousInputType = current.Input.Type;
        var inputChoices = ReorderChoices(
            new[] { "Serial", "Net", "Mock", "Console" },
            NormalizeInputType(current.Input.Type));

        current.Input.Type = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Choose the [green]input controller[/]:")
                .AddChoices(inputChoices));

        if (string.Equals(current.Input.Type, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            var portChoices = new List<string> { "Auto" };
            portChoices.AddRange(SerialPort.GetPortNames().OrderBy(port => port, StringComparer.OrdinalIgnoreCase));

            current.Input.ComPort = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Choose the [yellow]serial port[/]:")
                    .AddChoices(
                        ReorderChoices(
                            portChoices,
                            portChoices.Contains(current.Input.ComPort, StringComparer.OrdinalIgnoreCase) ? current.Input.ComPort : "Auto")));

            current.Input.BaudRate = AnsiConsole.Ask("Serial baud rate?", current.Input.BaudRate > 0 ? current.Input.BaudRate : 115200);
        }
        else if (string.Equals(current.Input.Type, "Net", StringComparison.OrdinalIgnoreCase))
        {
            current.Input.NetIp = AnsiConsole.Ask("KMBOX IP address?", string.IsNullOrWhiteSpace(current.Input.NetIp) ? "192.168.2.188" : current.Input.NetIp);
            current.Input.NetPort = AnsiConsole.Ask("KMBOX UDP port?", current.Input.NetPort > 0 ? current.Input.NetPort : 8006);
        }

        current.Bot.ReelTensionPauseThreshold = AnsiConsole.Ask(
            "Pause reeling tension threshold (0.0 - 1.0)?",
            current.Bot.ReelTensionPauseThreshold);

        current.Bot.ReelTensionResumeThreshold = AnsiConsole.Ask(
            "Resume reeling tension threshold (0.0 - 1.0)?",
            current.Bot.ReelTensionResumeThreshold);

        current.TickIntervalMs = AnsiConsole.Ask("Tick interval in milliseconds?", current.TickIntervalMs);

        MaybeUpdateSignalSourceDefault(current, previousInputType);

        current.SignalSource.ProcessName = string.IsNullOrWhiteSpace(current.SignalSource.ProcessName)
            ? "VRChat"
            : current.SignalSource.ProcessName;

        current.SignalSource.TargetObjectName = string.IsNullOrWhiteSpace(current.SignalSource.TargetObjectName)
            ? "FishingLogic"
            : current.SignalSource.TargetObjectName;

        current.Normalize();

        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(configPath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
        AnsiConsole.MarkupLine($"\n[green]Configuration saved to {configPath}.[/]");
        Thread.Sleep(1200);

        return current;
    }

    private static void MaybeUpdateSignalSourceDefault(AppConfig current, string previousInputType)
    {
        var previousDefault = GetRecommendedSignalSource(previousInputType);
        if (string.IsNullOrWhiteSpace(current.SignalSource.Type) ||
            string.Equals(current.SignalSource.Type, previousDefault, StringComparison.OrdinalIgnoreCase))
        {
            current.SignalSource.Type = GetRecommendedSignalSource(current.Input.Type);
        }
    }

    private static string GetRecommendedSignalSource(string? inputType) =>
        string.Equals(inputType, "Mock", StringComparison.OrdinalIgnoreCase)
            ? "Mock"
            : "Dma";

    private static string NormalizeInputType(string? inputType)
    {
        if (string.Equals(inputType, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            return "Serial";
        }

        if (string.Equals(inputType, "Net", StringComparison.OrdinalIgnoreCase))
        {
            return "Net";
        }

        if (string.Equals(inputType, "Console", StringComparison.OrdinalIgnoreCase))
        {
            return "Console";
        }

        return "Mock";
    }

    private static IReadOnlyList<string> ReorderChoices(IEnumerable<string> choices, string? preferredChoice)
    {
        var orderedChoices = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredChoice))
        {
            var preferred = choices.FirstOrDefault(choice => string.Equals(choice, preferredChoice, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                orderedChoices.Add(preferred);
            }
        }

        foreach (var choice in choices)
        {
            if (!orderedChoices.Contains(choice, StringComparer.OrdinalIgnoreCase))
            {
                orderedChoices.Add(choice);
            }
        }

        return orderedChoices;
    }
}
