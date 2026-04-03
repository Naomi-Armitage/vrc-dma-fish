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

    public static DashboardSnapshot CreateSnapshot(
        FishingBot bot,
        AppConfig config,
        LogLevel consoleLevel,
        LogLevel fileLevel,
        string? logFilePath,
        bool isRunning,
        string? statusNote = null)
    {
        return new DashboardSnapshot
        {
            UpdatedAtUtc = DateTime.UtcNow,
            IsRunning = isRunning,
            InputTypeText = GetInputTypeText(config.Input.Type),
            InputEndpointText = GetInputEndpoint(config),
            SignalSourceText = GetSignalSourceText(config.SignalSource.Type),
            StateText = GetStateText(bot.State),
            StateElapsedSeconds = bot.StateElapsed.TotalSeconds,
            IsHooked = bot.LastContext.IsHooked,
            CatchCompleted = bot.LastContext.CatchCompleted,
            Tension = Math.Clamp(bot.LastContext.Tension, 0f, 1f),
            HasPositionData = bot.LastContext.HasPositionData,
            FishCenterY = bot.LastContext.FishCenterY,
            BarCenterY = bot.LastContext.BarCenterY,
            BarHeight = bot.LastContext.BarHeight,
            ConsoleLogLevelText = LogLevelParser.ToDisplayName(consoleLevel),
            FileLogLevelText = string.IsNullOrWhiteSpace(logFilePath)
                ? "关闭"
                : LogLevelParser.ToDisplayName(fileLevel),
            LogFilePath = logFilePath,
            StatusNote = statusNote,
        };
    }

    public static DashboardSnapshot CreateDisconnectedSnapshot(
        string statusNote,
        LogLevel consoleLevel,
        LogLevel fileLevel,
        string? logFilePath)
    {
        return new DashboardSnapshot
        {
            UpdatedAtUtc = DateTime.UtcNow,
            IsRunning = false,
            StateText = "已停止",
            ConsoleLogLevelText = LogLevelParser.ToDisplayName(consoleLevel),
            FileLogLevelText = string.IsNullOrWhiteSpace(logFilePath)
                ? "关闭"
                : LogLevelParser.ToDisplayName(fileLevel),
            LogFilePath = logFilePath,
            StatusNote = statusNote,
        };
    }

    public static void Update(Layout layout, DashboardSnapshot snapshot)
    {
        var statusTable = new Table().Border(TableBorder.Rounded).Expand();
        statusTable.AddColumn("[cyan]项目[/]");
        statusTable.AddColumn("[cyan]数值[/]");
        statusTable.AddRow("输入方式", $"[green]{snapshot.InputTypeText}[/]");
        statusTable.AddRow("连接地址", $"[yellow]{Safe(snapshot.InputEndpointText)}[/]");
        statusTable.AddRow("信号来源", $"[green]{Safe(snapshot.SignalSourceText)}[/]");
        statusTable.AddRow("位置控制", snapshot.HasPositionData ? "[green]已就绪[/]" : "[grey]未就绪[/]");
        statusTable.AddRow("运行状态", snapshot.IsRunning ? "[green]运行中[/]" : "[grey]已停止[/]");
        statusTable.AddRow("机器人状态", $"[bold yellow]{Safe(snapshot.StateText)}[/]");
        statusTable.AddRow("状态时长", $"[grey]{snapshot.StateElapsedSeconds:F1}s[/]");
        statusTable.AddRow("控制台日志", $"[yellow]{Safe(snapshot.ConsoleLogLevelText)}[/]");
        statusTable.AddRow("文件日志", $"[yellow]{Safe(snapshot.FileLogLevelText)}[/]");

        layout["Left"].Update(
            new Panel(statusTable)
                .Header("[bold magenta] 系统状态 [/]") 
                .BorderColor(Color.Magenta));

        var tensionVal = snapshot.Tension * 100f;
        var tensionColor = tensionVal > 80 ? Color.Red : tensionVal > 50 ? Color.Yellow : Color.Green;

        var tensionBar = new BarChart()
            .Width(60)
            .Label($"[bold]当前张力: {tensionVal:F1}%[/]")
            .AddItem("张力", Math.Round(tensionVal), tensionColor);

        var hookStatus = snapshot.IsHooked
            ? "[bold red]鱼儿已咬钩[/]"
            : "[grey]等待鱼儿上钩...[/]";

        var positionStatus = snapshot.HasPositionData
            ? $"[cyan]鱼[/] {snapshot.FishCenterY:0.###}  [cyan]白条[/] {snapshot.BarCenterY:0.###}  [cyan]高度[/] {snapshot.BarHeight:0.###}"
            : "[grey]当前位置数据未就绪。[/]";

        var note = string.IsNullOrWhiteSpace(snapshot.StatusNote)
            ? "[grey]主窗口输出日志和 debug 信息。[/]"
            : $"[grey]{Safe(snapshot.StatusNote)}[/]";

        var logPath = string.IsNullOrWhiteSpace(snapshot.LogFilePath)
            ? "[grey]未启用日志文件[/]"
            : $"[grey]{Safe(snapshot.LogFilePath)}[/]";

        layout["Right"].Update(
            new Panel(
                new Rows(
                    new Padder(new Markup(hookStatus).Centered(), new Padding(0, 1)),
                    new Padder(new Markup(positionStatus).Centered(), new Padding(0, 0, 0, 1)),
                    tensionBar,
                    new Padder(new Markup(note), new Padding(0, 1, 0, 0)),
                    new Padder(new Markup($"日志文件: {logPath}"), new Padding(0, 0, 0, 1))))
                .Header("[bold cyan] 实时监控 [/]")
                .BorderColor(Color.Cyan1));
    }

    public static void Render(DashboardSnapshot snapshot)
    {
        var positionText = snapshot.HasPositionData
            ? $"鱼={snapshot.FishCenterY:0.###}, 白条={snapshot.BarCenterY:0.###}, 高度={snapshot.BarHeight:0.###}"
            : "位置=未就绪";

        AnsiConsole.MarkupLine(
            $"[cyan]状态：[/] {Safe(snapshot.StateText)} | [cyan]张力：[/] {snapshot.Tension:P1} | [cyan]输入：[/] {Safe(snapshot.InputTypeText)} | [cyan]信号：[/] {Safe(snapshot.SignalSourceText)} | [cyan]{Safe(positionText)}[/]");
    }

    public static string GetInputEndpoint(AppConfig config)
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

    public static string GetInputTypeText(string? type)
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

    public static string GetSignalSourceText(string? type)
    {
        if (string.Equals(type, "Dma", StringComparison.OrdinalIgnoreCase))
        {
            return "DMA";
        }

        if (string.Equals(type, "Screen", StringComparison.OrdinalIgnoreCase))
        {
            return "屏幕";
        }

        return "模拟";
    }

    public static string GetStateText(FishState state) => state switch
    {
        FishState.Idle => "待机",
        FishState.Casting => "抛竿中",
        FishState.Waiting => "等待上钩",
        FishState.Hooked => "咬钩确认",
        FishState.Reeling => "收线中",
        FishState.Cooldown => "冷却中",
        _ => state.ToString(),
    };

    private static string Safe(string? value) => Markup.Escape(value ?? string.Empty);
}
