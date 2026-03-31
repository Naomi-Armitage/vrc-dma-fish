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

        Logger.Info("CONFIG", $"Using config: {configPath}");

        var input = CreateInputController(config.Input);
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

            var ticksRemaining = options.MaxTicks;
            while (!cancelled && (!ticksRemaining.HasValue || ticksRemaining > 0))
            {
                bot.Tick();
                Dashboard.Render(bot);
                Thread.Sleep(config.TickIntervalMs);

                if (ticksRemaining.HasValue)
                {
                    ticksRemaining--;
                }
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

    private static IInputController CreateInputController(InputConfig config)
    {
        if (string.Equals(config.Type, "Console", StringComparison.OrdinalIgnoreCase))
        {
            return new ConsoleInputController();
        }

        Logger.Warn("CONFIG", $"Unsupported input controller '{config.Type}'. Falling back to Console.");
        return new ConsoleInputController();
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

    private sealed record RuntimeOptions(string ConfigPath, int? MaxTicks)
    {
        public static RuntimeOptions Parse(string[] args)
        {
            var configPath = "appsettings.json";
            int? maxTicks = null;

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
                }
            }

            return new RuntimeOptions(configPath, maxTicks);
        }
    }
}
