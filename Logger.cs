namespace VrcDmaFish;

public static class Logger
{
    public static void Info(string scope, string message)
        => Write("INFO", scope, message, ConsoleColor.Cyan);

    public static void Warn(string scope, string message)
        => Write("WARN", scope, message, ConsoleColor.Yellow);

    public static void Error(string scope, string message)
        => Write("ERRO", scope, message, ConsoleColor.Red);

    private static void Write(string level, string scope, string message, ConsoleColor color)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write($"[{DateTime.Now:HH:mm:ss}] [{level}] [{scope}] ");
        Console.ForegroundColor = old;
        Console.WriteLine(message);
    }
}
