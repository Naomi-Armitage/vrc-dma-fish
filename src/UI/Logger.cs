using VrcDmaFish.Models;

namespace VrcDmaFish.UI;

public static class Logger
{
    private static readonly object Sync = new();
    private static bool _consoleEnabled = true;
    private static string? _logFilePath;
    private static LogLevel _consoleLevel = LogLevel.Info;
    private static LogLevel _fileLevel = LogLevel.Info;

    public static bool IsDebugEnabled =>
        _consoleLevel <= LogLevel.Debug ||
        (!string.IsNullOrWhiteSpace(_logFilePath) && _fileLevel <= LogLevel.Debug);

    public static string? LogFilePath => _logFilePath;

    public static LogLevel ConsoleLevel => _consoleLevel;

    public static LogLevel FileLevel => _fileLevel;

    public static void Configure(
        LogLevel consoleLevel,
        string? logFilePath,
        LogLevel? fileLevel = null,
        bool enableConsole = true)
    {
        lock (Sync)
        {
            _consoleLevel = consoleLevel;
            _fileLevel = fileLevel ?? consoleLevel;
            _consoleEnabled = enableConsole;
            _logFilePath = string.IsNullOrWhiteSpace(logFilePath) ? null : Path.GetFullPath(logFilePath);

            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }
    }

    public static bool TryParseLevel(string? text, out LogLevel level) => LogLevelParser.TryParse(text, out level);

    public static string GetLevelDisplayName(LogLevel level) => LogLevelParser.ToDisplayName(level);

    public static void Debug(string tag, string message) => Write(LogLevel.Debug, "DEBUG", tag, message);

    public static void Info(string tag, string message) => Write(LogLevel.Info, "INFO", tag, message);

    public static void Warn(string tag, string message) => Write(LogLevel.Warn, "WARN", tag, message);

    public static void Error(string tag, string message) => Write(LogLevel.Error, "ERROR", tag, message);

    private static void Write(LogLevel level, string levelText, string tag, string message)
    {
        var shouldWriteConsole = _consoleEnabled && level >= _consoleLevel && _consoleLevel != LogLevel.None;
        var shouldWriteFile =
            !string.IsNullOrWhiteSpace(_logFilePath) &&
            level >= _fileLevel &&
            _fileLevel != LogLevel.None;

        if (!shouldWriteConsole && !shouldWriteFile)
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] [{levelText}] [{tag}] {message}";

        lock (Sync)
        {
            if (shouldWriteConsole)
            {
                Console.WriteLine(line);
            }

            if (shouldWriteFile && !string.IsNullOrWhiteSpace(_logFilePath))
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
    }
}
