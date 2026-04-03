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
        AnsiConsole.MarkupLine("[bold cyan]交互式配置[/]\n");

        if (!AnsiConsole.Confirm("要编辑当前配置吗？", false))
        {
            return current;
        }

        var previousInputType = current.Input.Type;
        var inputChoices = ReorderChoices(
            new[] { "Serial", "Net", "Mock", "Console" },
            NormalizeInputType(current.Input.Type));

        current.Input.Type = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("请选择[green]输入控制器[/]：")
                .AddChoices(inputChoices));

        if (string.Equals(current.Input.Type, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            var portChoices = new List<string> { "Auto" };
            portChoices.AddRange(SerialPort.GetPortNames().OrderBy(port => port, StringComparer.OrdinalIgnoreCase));

            current.Input.ComPort = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("请选择[yellow]串口端口[/]：")
                    .AddChoices(
                        ReorderChoices(
                            portChoices,
                            portChoices.Contains(current.Input.ComPort, StringComparer.OrdinalIgnoreCase) ? current.Input.ComPort : "Auto")));

            current.Input.BaudRate = AnsiConsole.Ask("串口波特率？", current.Input.BaudRate > 0 ? current.Input.BaudRate : 115200);
        }
        else if (string.Equals(current.Input.Type, "Net", StringComparison.OrdinalIgnoreCase))
        {
            current.Input.NetIp = AnsiConsole.Ask("KMBOX IP 地址？", string.IsNullOrWhiteSpace(current.Input.NetIp) ? "192.168.2.188" : current.Input.NetIp);
            current.Input.NetPort = AnsiConsole.Ask("KMBOX UDP 端口？", current.Input.NetPort > 0 ? current.Input.NetPort : 8006);
        }

        current.Bot.ReelTensionPauseThreshold = AnsiConsole.Ask(
            "收线暂停张力阈值（0.0 - 1.0）？",
            current.Bot.ReelTensionPauseThreshold);

        current.Bot.ReelTensionResumeThreshold = AnsiConsole.Ask(
            "收线恢复张力阈值（0.0 - 1.0）？",
            current.Bot.ReelTensionResumeThreshold);

        current.Bot.HookClickMs = AnsiConsole.Ask(
            "咬钩时点击时长（毫秒）？",
            current.Bot.HookClickMs);

        current.Bot.ReelPulseMs = AnsiConsole.Ask(
            "基础收线按住时长（毫秒）？",
            current.Bot.ReelPulseMs);

        current.Bot.ReelHoldGainMs = AnsiConsole.Ask(
            "张力修正增益（毫秒）？",
            current.Bot.ReelHoldGainMs);

        current.Bot.ReelVelocityDampingMs = AnsiConsole.Ask(
            "张力变化阻尼（毫秒）？",
            current.Bot.ReelVelocityDampingMs);

        current.TickIntervalMs = AnsiConsole.Ask("轮询间隔（毫秒）？", current.TickIntervalMs);

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
        AnsiConsole.MarkupLine($"\n[green]配置已保存到 {configPath}。[/]");
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
