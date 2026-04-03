namespace VrcDmaFish.Models;

public sealed class FishContext
{
    public bool IsHooked { get; set; }
    public bool CatchCompleted { get; set; }
    public float Tension { get; set; }
    public float? FishCenterY { get; set; }
    public float? BarCenterY { get; set; }
    public float? BarHeight { get; set; }

    public bool HasPositionData =>
        FishCenterY.HasValue &&
        BarCenterY.HasValue &&
        BarHeight.HasValue &&
        BarHeight.Value > 0f;
}
