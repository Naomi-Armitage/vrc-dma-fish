namespace VrcDmaFish.UI;

public static class Logger
{
    private static readonly object Sync = new();
    private static bool _debugEnabled;
    private static bool _consoleEnabled = true;
    private static string? _logFilePath;

    public static bool IsDebugEnabled => _debugEnabled;

    public static string? LogFilePath => _logFilePath;

    public static void Configure(bool debugEnabled, string? logFilePath, bool enableConsole = true)
    {
        lock (Sync)
        {
            _debugEnabled = debugEnabled;
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

    public static void Debug(string tag, string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Write("DEBUG", tag, message);
    }

    public static void Info(string tag, string message) => Write("INFO", tag, message);

    public static void Warn(string tag, string message) => Write("WARN", tag, message);

    public static void Error(string tag, string message) => Write("ERROR", tag, message);

    private static void Write(string level, string tag, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] [{tag}] {message}";

        lock (Sync)
        {
            if (_consoleEnabled)
            {
                Console.WriteLine(line);
            }

            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
    }
}
