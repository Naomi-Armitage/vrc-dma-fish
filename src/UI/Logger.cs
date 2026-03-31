namespace VrcDmaFish.UI;
public static class Logger {
    public static void Info(string t, string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INFO] [{t}] {m}");
    public static void Warn(string t, string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [WARN] [{t}] {m}");
    public static void Error(string t, string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ERROR] [{t}] {m}");
}
