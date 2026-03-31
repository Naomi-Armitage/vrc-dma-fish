namespace VrcDmaFish;

public sealed class FishContext
{
    public bool IsHooked { get; set; }
    public bool CatchCompleted { get; set; }
    public double Tension { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public override string ToString()
        => $"hooked={IsHooked}, caught={CatchCompleted}, tension={Tension:F2}, ts={Timestamp:HH:mm:ss.fff}";
}
