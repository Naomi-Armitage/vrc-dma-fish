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
        var configPath = Path.GetFullPath(options.ConfigPath);

        AppConfig config;
        try
        {
            config = AppConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            Logger.Error("CONFIG", $"Failed to load {configPath}: {ex.Message}");
            return 1;
        }

        foreach (var warning in config.Normalize())
        {
            Logger.Warn("CONFIG", warning);
        }

        if (options.RunWizard && IsInteractiveConsole())
        {
            try
            {
                config = ConfigWizard.Run(config, configPath);
                foreach (var warning in config.Normalize())
                {
                    Logger.Warn("CONFIG", warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("CONFIG", $"Interactive configuration failed: {ex.Message}");
                return 1;
            }
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
                Logger.Info("SYS", "Stop requested.");
            };

            if (IsInteractiveConsole())
            {
                RunDashboardLoop(bot, config, options.MaxTicks, () => cancelled);
            }
            else
            {
                RunPlainLoop(bot, config, options.MaxTicks, () => cancelled);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("SYS", $"Unhandled fatal error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (signalSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static bool IsInteractiveConsole() => Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;

    private static void RunDashboardLoop(FishingBot bot, AppConfig config, int? maxTicks, Func<bool> shouldStop)
    {
        var layout = Dashboard.CreateLayout();
        var ticksRemaining = maxTicks;

        AnsiConsole.Live(layout).AutoClear(false).Start(ctx =>
        {
            while (!shouldStop() && (!ticksRemaining.HasValue || ticksRemaining > 0))
            {
                bot.Tick();
                Dashboard.Update(layout, bot, config);
                ctx.Refresh();
                Thread.Sleep(config.TickIntervalMs);

                if (ticksRemaining.HasValue)
                {
                    ticksRemaining--;
                }
            }
        });
    }

    private static void RunPlainLoop(FishingBot bot, AppConfig config, int? maxTicks, Func<bool> shouldStop)
    {
        var ticksRemaining = maxTicks;

        while (!shouldStop() && (!ticksRemaining.HasValue || ticksRemaining > 0))
        {
            bot.Tick();
            Dashboard.Render(bot, config);
            Thread.Sleep(config.TickIntervalMs);

            if (ticksRemaining.HasValue)
            {
                ticksRemaining--;
            }
        }
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

        Logger.Warn("CONFIG", $"Unsupported input controller '{config.Type}'. Falling back to Mock.");
        return new MockInputController();
    }

    private static IFishSignalSource CreateSignalSource(SignalSourceConfig config)
    {
        if (string.Equals(config.Type, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            return new MockFishSignalSource();
        }

        if (string.Equals(config.Type, "Dma", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
            {
                Logger.Warn("CONFIG", "DMA mode is only supported on Windows. Falling back to Mock.");
                return new MockFishSignalSource();
            }

            var dmaProvider = new DmaProvider(config);
            if (dmaProvider.IsReady)
            {
                return dmaProvider;
            }

            Logger.Warn("CONFIG", "DMA source is not ready. Falling back to Mock.");
            dmaProvider.Dispose();
            return new MockFishSignalSource();
        }

        Logger.Warn("CONFIG", $"Unsupported signal source '{config.Type}'. Falling back to Mock.");
        return new MockFishSignalSource();
    }

    private sealed record RuntimeOptions(string ConfigPath, int? MaxTicks, bool RunWizard)
    {
        public static RuntimeOptions Parse(string[] args)
        {
            var configPath = "appsettings.json";
            int? maxTicks = null;
            var runWizard = true;

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
                        Logger.Warn("CONFIG", $"Ignoring invalid tick count '{args[i]}'.");
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

            return new RuntimeOptions(configPath, maxTicks, runWizard);
        }
    }
}
