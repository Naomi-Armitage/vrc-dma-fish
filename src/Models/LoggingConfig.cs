namespace VrcDmaFish.Models;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    None = 4,
}

public static class LogLevelParser
{
    public static bool TryParse(string? text, out LogLevel level)
    {
        level = LogLevel.Info;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "debug":
                level = LogLevel.Debug;
                return true;
            case "info":
                level = LogLevel.Info;
                return true;
            case "warn":
            case "warning":
                level = LogLevel.Warn;
                return true;
            case "error":
                level = LogLevel.Error;
                return true;
            case "none":
            case "off":
            case "quiet":
                level = LogLevel.None;
                return true;
            default:
                return false;
        }
    }

    public static string ToDisplayName(LogLevel level) => level switch
    {
        LogLevel.Debug => "Debug",
        LogLevel.Info => "Info",
        LogLevel.Warn => "Warn",
        LogLevel.Error => "Error",
        LogLevel.None => "None",
        _ => level.ToString(),
    };
}

public sealed class LoggingConfig
{
    public string Level { get; set; } = "Info";
    public string? FileLevel { get; set; }
    public string? FilePath { get; set; }
}
