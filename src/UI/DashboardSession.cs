using System.Text.Json;
using VrcDmaFish.Core;
using VrcDmaFish.Models;

namespace VrcDmaFish.UI;

public sealed class DashboardSnapshot
{
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRunning { get; set; }
    public string InputTypeText { get; set; } = "模拟";
    public string InputEndpointText { get; set; } = "模拟";
    public string SignalSourceText { get; set; } = "模拟";
    public string StateText { get; set; } = FishState.Idle.ToString();
    public double StateElapsedSeconds { get; set; }
    public bool IsHooked { get; set; }
    public bool CatchCompleted { get; set; }
    public float Tension { get; set; }
    public bool HasPositionData { get; set; }
    public float? FishCenterY { get; set; }
    public float? BarCenterY { get; set; }
    public float? BarHeight { get; set; }
    public string ConsoleLogLevelText { get; set; } = "Info";
    public string FileLogLevelText { get; set; } = "Info";
    public string? LogFilePath { get; set; }
    public string? StatusNote { get; set; }
}

public sealed class DashboardSessionWriter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _sync = new();

    public DashboardSessionWriter(string snapshotPath)
    {
        SnapshotPath = snapshotPath;
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath) ?? Directory.GetCurrentDirectory());
    }

    public string SnapshotPath { get; }

    public void Write(DashboardSnapshot snapshot)
    {
        lock (_sync)
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var tempPath = SnapshotPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SnapshotPath, true);
        }
    }

    public void Dispose()
    {
    }

    public static DashboardSnapshot? TryRead(string snapshotPath)
    {
        if (!File.Exists(snapshotPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DashboardSnapshot>(File.ReadAllText(snapshotPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
