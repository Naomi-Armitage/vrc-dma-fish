using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using Spectre.Console;
using VrcDmaFish.Core;
using VrcDmaFish.Inputs;
using VrcDmaFish.Models;
using VrcDmaFish.Providers;
using VrcDmaFish.UI;

namespace VrcDmaFish;

public static class Program
{
    public static int Main(string[] args)
    {
        var options = RuntimeOptions.Parse(args);

        if (options.DashboardClientSnapshotPath is not null)
        {
            Logger.Configure(LogLevel.None, null, enableConsole: false);
            return RunDashboardClient(options.DashboardClientSnapshotPath);
        }

        Logger.Configure(options.GetBootstrapConsoleLevel(), null, options.GetBootstrapFileLevel());

        var configPath = Path.GetFullPath(options.ConfigPath);

        AppConfig config;
        try
        {
            config = AppConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            Logger.Error("配置", $"加载配置文件失败 {configPath}: {ex.Message}");
            return 1;
        }

        var normalizeWarnings = config.Normalize();
        ApplyLoggingConfiguration(config, options);

        foreach (var warning in normalizeWarnings)
        {
            Logger.Warn("配置", warning);
        }

        if (options.RunWizard && IsInteractiveConsole())
        {
            try
            {
                config = ConfigWizard.Run(config, configPath);
                normalizeWarnings = config.Normalize();
                ApplyLoggingConfiguration(config, options);

                foreach (var warning in normalizeWarnings)
                {
                    Logger.Warn("配置", warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("配置", $"交互式配置失败: {ex.Message}");
                return 1;
            }
        }

        if (options.DumpObjectsLimit.HasValue)
        {
            if (!OperatingSystem.IsWindows())
            {
                Logger.Error("调试", "对象转储仅支持 Windows。");
                return 1;
            }

            return RunUnityDump(config.SignalSource, options.DumpObjectsLimit.Value);
        }

        DashboardSessionWriter? dashboardWriter = null;
        try
        {
            if (options.UseSeparateUiWindow && IsInteractiveConsole() && OperatingSystem.IsWindows())
            {
                dashboardWriter = TryStartDashboardWindow();
            }

            using var input = CreateInputController(config.Input);
            var signalSource = CreateSignalSource(config.SignalSource);

            try
            {
                var bot = new FishingBot(signalSource, input, config.Bot);
                var cancelled = false;

                Console.CancelKeyPress += (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    cancelled = true;
                    Logger.Info("系统", "收到停止请求。");
                };

                if (dashboardWriter is not null)
                {
                    RunSeparateDashboardLoop(bot, config, options.MaxTicks, () => cancelled, dashboardWriter);
                }
                else if (IsInteractiveConsole())
                {
                    RunInlineDashboardLoop(bot, config, options.MaxTicks, () => cancelled);
                }
                else
                {
                    RunPlainLoop(bot, config, options.MaxTicks, () => cancelled);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("系统", $"程序发生未处理的致命错误: {ex.Message}");
                throw;
            }
            finally
            {
                if (signalSource is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        catch (Exception)
        {
            return 1;
        }
        finally
        {
            if (dashboardWriter is not null)
            {
                dashboardWriter.Write(
                    Dashboard.CreateDisconnectedSnapshot(
                        "主程序已退出，调试输出请查看主窗口或日志文件。",
                        Logger.ConsoleLevel,
                        Logger.FileLevel,
                        Logger.LogFilePath));
                dashboardWriter.Dispose();
            }
        }
    }

    private static bool IsInteractiveConsole() =>
        Environment.UserInteractive &&
        !Console.IsInputRedirected &&
        !Console.IsOutputRedirected;

    private static void RunInlineDashboardLoop(FishingBot bot, AppConfig config, int? maxTicks, Func<bool> shouldStop)
    {
        var layout = Dashboard.CreateLayout();
        var ticksRemaining = maxTicks;

        AnsiConsole.Live(layout).AutoClear(false).Start(ctx =>
        {
            while (!shouldStop() && (!ticksRemaining.HasValue || ticksRemaining > 0))
            {
                bot.Tick();
                Dashboard.Update(
                    layout,
                    Dashboard.CreateSnapshot(
                        bot,
                        config,
                        Logger.ConsoleLevel,
                        Logger.FileLevel,
                        Logger.LogFilePath,
                        isRunning: true));
                ctx.Refresh();
                Thread.Sleep(config.TickIntervalMs);

                if (ticksRemaining.HasValue)
                {
                    ticksRemaining--;
                }
            }
        });
    }

    private static void RunSeparateDashboardLoop(
        FishingBot bot,
        AppConfig config,
        int? maxTicks,
        Func<bool> shouldStop,
        DashboardSessionWriter dashboardWriter)
    {
        var ticksRemaining = maxTicks;
        Logger.Info("UI", "独立监控窗口已启动，当前窗口将输出日志和 debug 信息。");

        while (!shouldStop() && (!ticksRemaining.HasValue || ticksRemaining > 0))
        {
            bot.Tick();
            dashboardWriter.Write(
                Dashboard.CreateSnapshot(
                    bot,
                    config,
                    Logger.ConsoleLevel,
                    Logger.FileLevel,
                    Logger.LogFilePath,
                    isRunning: true,
                    statusNote: "主窗口输出日志和 debug 详细信息。"));
            Thread.Sleep(config.TickIntervalMs);

            if (ticksRemaining.HasValue)
            {
                ticksRemaining--;
            }
        }
    }

    private static void RunPlainLoop(FishingBot bot, AppConfig config, int? maxTicks, Func<bool> shouldStop)
    {
        var ticksRemaining = maxTicks;

        while (!shouldStop() && (!ticksRemaining.HasValue || ticksRemaining > 0))
        {
            bot.Tick();
            Dashboard.Render(
                Dashboard.CreateSnapshot(
                    bot,
                    config,
                    Logger.ConsoleLevel,
                    Logger.FileLevel,
                    Logger.LogFilePath,
                    isRunning: true));
            Thread.Sleep(config.TickIntervalMs);

            if (ticksRemaining.HasValue)
            {
                ticksRemaining--;
            }
        }
    }

    private static int RunDashboardClient(string snapshotPath)
    {
        var layout = Dashboard.CreateLayout();
        var lastSnapshot = Dashboard.CreateDisconnectedSnapshot("等待主程序写入监控快照...", LogLevel.Info, LogLevel.None, null);
        try
        {
            AnsiConsole.Live(layout).AutoClear(false).Start(ctx =>
            {
                while (true)
                {
                    var snapshot = DashboardSessionWriter.TryRead(snapshotPath) ?? lastSnapshot;
                    lastSnapshot = snapshot;
                    Dashboard.Update(layout, snapshot);
                    ctx.Refresh();

                    if (!snapshot.IsRunning &&
                        (DateTime.UtcNow - snapshot.UpdatedAtUtc) > TimeSpan.FromSeconds(2))
                    {
                        break;
                    }

                    Thread.Sleep(250);
                }
            });
        }
        catch (IOException)
        {
            var snapshot = DashboardSessionWriter.TryRead(snapshotPath) ?? lastSnapshot;
            Console.WriteLine("VrcDmaFish Dashboard");
            Console.WriteLine($"状态: {snapshot.StateText}");
            Console.WriteLine($"信号: {snapshot.SignalSourceText}");
            Console.WriteLine($"张力: {snapshot.Tension:P1}");
            Console.WriteLine($"位置: {(snapshot.HasPositionData ? $"鱼={snapshot.FishCenterY:0.###}, 白条={snapshot.BarCenterY:0.###}, 高度={snapshot.BarHeight:0.###}" : "未就绪")}");
            if (!string.IsNullOrWhiteSpace(snapshot.StatusNote))
            {
                Console.WriteLine($"说明: {snapshot.StatusNote}");
            }
        }

        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static int RunUnityDump(SignalSourceConfig config, int limit)
    {
        if (!string.Equals(config.Type, "Dma", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Error("调试", "对象转储仅支持 DMA 模式。");
            return 1;
        }

        using var provider = new DmaProvider(config);
        if (!provider.HasConnectedProcess)
        {
            Logger.Error("调试", "DMA 未连接到目标进程，无法执行对象转储。");
            return 1;
        }

        var gameObjectManagerAddress = provider.ResolveGameObjectManagerAddress();
        if (gameObjectManagerAddress == 0)
        {
            Logger.Warn("调试", "未能解析 GameObjectManager 地址。你可以先尝试配置 GameObjectManagerPattern / GameObjectManagerAddress。");
        }
        else
        {
            Logger.Info("调试", $"GameObjectManager 地址: 0x{gameObjectManagerAddress:X}");
        }

        var objects = provider.DumpUnityObjects(limit);
        if (objects.Count == 0)
        {
            Logger.Warn("调试", "未能转储任何 Unity 对象。");
            return 1;
        }

        foreach (var entry in objects)
        {
            Logger.Info(
                "对象",
                $"name='{entry.Name}' gameObject=0x{entry.GameObjectAddress:X} node=0x{entry.NodeAddress:X} namePtr=0x{entry.NamePointer:X}");
        }

        return 0;
    }

    private static DashboardSessionWriter? TryStartDashboardWindow()
    {
        try
        {
            var sessionDirectory = Path.Combine(
                Path.GetTempPath(),
                "VrcDmaFish",
                "dashboard",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N"));

            var snapshotPath = Path.Combine(sessionDirectory, "snapshot.json");
            var writer = new DashboardSessionWriter(snapshotPath);
            writer.Write(new DashboardSnapshot
            {
                UpdatedAtUtc = DateTime.UtcNow,
                IsRunning = true,
                StateText = "准备中",
                ConsoleLogLevelText = Logger.GetLevelDisplayName(Logger.ConsoleLevel),
                FileLogLevelText = Logger.LogFilePath is null
                    ? "关闭"
                    : Logger.GetLevelDisplayName(Logger.FileLevel),
                LogFilePath = Logger.LogFilePath,
                StatusNote = "独立监控窗口准备中...",
            });

            if (!TryLaunchDashboardProcess(snapshotPath))
            {
                Logger.Warn("UI", "启动独立监控窗口失败，已回退到当前窗口显示。");
                writer.Dispose();
                return null;
            }

            return writer;
        }
        catch (Exception ex)
        {
            Logger.Warn("UI", $"创建独立监控窗口失败: {ex.Message}");
            return null;
        }
    }

    private static bool TryLaunchDashboardProcess(string snapshotPath)
    {
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return false;
        }

        var escapedSnapshotPath = EscapePowerShellSingleQuotedString(snapshotPath);
        var escapedEntryAssemblyPath = EscapePowerShellSingleQuotedString(entryAssemblyPath);

        string command;
        if (entryAssemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            command =
                $"$Host.UI.RawUI.WindowTitle='VrcDmaFish Dashboard'; dotnet '{escapedEntryAssemblyPath}' --dashboard-client '{escapedSnapshotPath}'";
        }
        else
        {
            command =
                $"$Host.UI.RawUI.WindowTitle='VrcDmaFish Dashboard'; & '{escapedEntryAssemblyPath}' --dashboard-client '{escapedSnapshotPath}'";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -ExecutionPolicy Bypass -Command \"{command}\"",
            UseShellExecute = true,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };

        Process.Start(startInfo);
        return true;
    }

    private static string EscapePowerShellSingleQuotedString(string value) => value.Replace("'", "''");

    private static void ApplyLoggingConfiguration(AppConfig config, RuntimeOptions options)
    {
        var loggingSettings = ResolveLoggingSettings(config.Logging, options);
        Logger.Configure(loggingSettings.ConsoleLevel, loggingSettings.LogFilePath, loggingSettings.FileLevel);

        foreach (var warning in loggingSettings.Warnings)
        {
            Logger.Warn("配置", warning);
        }

        if (Logger.IsDebugEnabled)
        {
            Logger.Debug(
                "系统",
                $"Debug 模式已开启。控制台日志级别={Logger.GetLevelDisplayName(Logger.ConsoleLevel)}，文件日志级别={Logger.GetLevelDisplayName(Logger.FileLevel)}。");
        }
    }

    private static ResolvedLoggingSettings ResolveLoggingSettings(LoggingConfig config, RuntimeOptions options)
    {
        var warnings = new List<string>();
        var consoleLevel = !string.IsNullOrWhiteSpace(options.LogLevelText)
            ? ResolveCliLogLevel(options.LogLevelText, LogLevel.Info, "--log-level", warnings)
            : options.DebugEnabled
                ? LogLevel.Debug
                : ResolveConfiguredLogLevel(config.Level, LogLevel.Info, "Logging.Level", warnings);

        var fileLevel = !string.IsNullOrWhiteSpace(options.FileLogLevelText)
            ? ResolveCliLogLevel(options.FileLogLevelText, consoleLevel, "--file-log-level", warnings)
            : options.DebugEnabled
                ? LogLevel.Debug
                : ResolveConfiguredLogLevel(config.FileLevel, consoleLevel, "Logging.FileLevel", warnings);

        var logFilePath = ResolveLogFilePath(options.LogFilePath, config.FilePath, fileLevel);
        return new ResolvedLoggingSettings(consoleLevel, fileLevel, logFilePath, warnings);
    }

    private static LogLevel ResolveCliLogLevel(string cliText, LogLevel defaultLevel, string cliName, List<string> warnings)
    {
        if (LogLevelParser.TryParse(cliText, out var cliLevel))
        {
            return cliLevel;
        }

        warnings.Add($"{cliName}='{cliText}' 无效，已回退为 {LogLevelParser.ToDisplayName(defaultLevel)}。");
        return defaultLevel;
    }

    private static LogLevel ResolveConfiguredLogLevel(
        string? configText,
        LogLevel defaultLevel,
        string configName,
        List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(configText))
        {
            if (LogLevelParser.TryParse(configText, out var configLevel))
            {
                return configLevel;
            }

            warnings.Add($"{configName}='{configText}' 无效，已回退为 {LogLevelParser.ToDisplayName(defaultLevel)}。");
        }

        return defaultLevel;
    }

    private static string? ResolveLogFilePath(string? cliPath, string? configuredPath, LogLevel fileLevel)
    {
        if (fileLevel == LogLevel.None)
        {
            return null;
        }

        var resolvedPath = !string.IsNullOrWhiteSpace(cliPath) ? cliPath : configuredPath;
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            return Path.GetFullPath(resolvedPath);
        }

        var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        return Path.Combine(logDirectory, $"vrcdmafish-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    private static IInputController CreateInputController(InputConfig config)
    {
        if (string.Equals(config.Type, "Console", StringComparison.OrdinalIgnoreCase))
        {
            return new ConsoleInputController();
        }

        if (string.Equals(config.Type, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            return new KmboxInputController(config.ComPort, config.BaudRate);
        }

        if (string.Equals(config.Type, "Net", StringComparison.OrdinalIgnoreCase))
        {
            return new KmboxNetInputController(config.NetIp, config.NetPort);
        }

        if (string.Equals(config.Type, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            return new MockInputController();
        }

        Logger.Warn("配置", $"不支持的输入控制器 '{config.Type}'，已回退到 Mock。");
        return new MockInputController();
    }

    private static IFishSignalSource CreateSignalSource(SignalSourceConfig config)
    {
        if (string.Equals(config.Type, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            config.Type = "Mock";
            return new MockFishSignalSource();
        }

        if (string.Equals(config.Type, "Dma", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
            {
                Logger.Warn("配置", "DMA 模式仅支持 Windows，已回退到 Mock。");
                config.Type = "Mock";
                return new MockFishSignalSource();
            }

            var dmaProvider = new DmaProvider(config);
            if (dmaProvider.IsReady)
            {
                config.Type = "Dma";
                return dmaProvider;
            }

            Logger.Warn("配置", "DMA 信号源未就绪，已回退到 Mock。");
            dmaProvider.Dispose();
            config.Type = "Mock";
            return new MockFishSignalSource();
        }

        Logger.Warn("配置", $"不支持的信号源 '{config.Type}'，已回退到 Mock。");
        config.Type = "Mock";
        return new MockFishSignalSource();
    }

    private sealed record RuntimeOptions(
        string ConfigPath,
        int? MaxTicks,
        bool RunWizard,
        bool DebugEnabled,
        string? LogLevelText,
        string? FileLogLevelText,
        string? LogFilePath,
        bool UseSeparateUiWindow,
        string? DashboardClientSnapshotPath,
        int? DumpObjectsLimit)
    {
        public static RuntimeOptions Parse(string[] args)
        {
            var configPath = "appsettings.json";
            int? maxTicks = null;
            var runWizard = true;
            var debugEnabled = false;
            string? logLevelText = null;
            string? fileLogLevelText = null;
            string? logFilePath = null;
            var useSeparateUiWindow = true;
            string? dashboardClientSnapshotPath = null;
            int? dumpObjectsLimit = null;

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    configPath = args[++i];
                    continue;
                }

                if (string.Equals(args[i], "--ticks", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out var parsedTicks) && parsedTicks >= 0)
                    {
                        maxTicks = parsedTicks;
                    }
                    else
                    {
                        Logger.Warn("配置", $"忽略无效的 tick 次数 '{args[i]}'。");
                    }

                    continue;
                }

                if (string.Equals(args[i], "--debug", StringComparison.OrdinalIgnoreCase))
                {
                    debugEnabled = true;
                    continue;
                }

                if (string.Equals(args[i], "--log-level", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    logLevelText = args[++i];
                    continue;
                }

                if (string.Equals(args[i], "--file-log-level", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    fileLogLevelText = args[++i];
                    continue;
                }

                if (string.Equals(args[i], "--log-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    logFilePath = args[++i];
                    continue;
                }

                if (string.Equals(args[i], "--no-ui-window", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[i], "--inline-dashboard", StringComparison.OrdinalIgnoreCase))
                {
                    useSeparateUiWindow = false;
                    continue;
                }

                if (string.Equals(args[i], "--dashboard-client", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    dashboardClientSnapshotPath = Path.GetFullPath(args[++i]);
                    runWizard = false;
                    useSeparateUiWindow = false;
                    continue;
                }

                if (string.Equals(args[i], "--dump-objects", StringComparison.OrdinalIgnoreCase))
                {
                    dumpObjectsLimit = 128;
                    runWizard = false;

                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedLimit) && parsedLimit > 0)
                    {
                        dumpObjectsLimit = parsedLimit;
                        i++;
                    }

                    continue;
                }

                if (string.Equals(args[i], "--no-wizard", StringComparison.OrdinalIgnoreCase))
                {
                    runWizard = false;
                    continue;
                }

                if (string.Equals(args[i], "--wizard", StringComparison.OrdinalIgnoreCase))
                {
                    runWizard = true;
                }
            }

            return new RuntimeOptions(
                configPath,
                maxTicks,
                runWizard,
                debugEnabled,
                logLevelText,
                fileLogLevelText,
                logFilePath,
                useSeparateUiWindow,
                dashboardClientSnapshotPath,
                dumpObjectsLimit);
        }

        public LogLevel GetBootstrapConsoleLevel()
        {
            if (!string.IsNullOrWhiteSpace(LogLevelText) && LogLevelParser.TryParse(LogLevelText, out var configuredLevel))
            {
                return configuredLevel;
            }

            return DebugEnabled ? LogLevel.Debug : LogLevel.Info;
        }

        public LogLevel GetBootstrapFileLevel()
        {
            if (!string.IsNullOrWhiteSpace(FileLogLevelText) && LogLevelParser.TryParse(FileLogLevelText, out var configuredFileLevel))
            {
                return configuredFileLevel;
            }

            if (!string.IsNullOrWhiteSpace(LogLevelText) && LogLevelParser.TryParse(LogLevelText, out var configuredLevel))
            {
                return configuredLevel;
            }

            return DebugEnabled ? LogLevel.Debug : LogLevel.Info;
        }
    }

    private sealed record ResolvedLoggingSettings(
        LogLevel ConsoleLevel,
        LogLevel FileLevel,
        string? LogFilePath,
        IReadOnlyList<string> Warnings);
}
