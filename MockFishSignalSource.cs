namespace VrcDmaFish;

public sealed class MockFishSignalSource : IFishSignalSource
{
    private readonly Random _random = new();
    private DateTime _cycleStart = DateTime.Now;
    private bool _hookedTriggered;
    private bool _caught;

    public FishContext Read()
    {
        var elapsed = DateTime.Now - _cycleStart;
        var seconds = elapsed.TotalSeconds;

        if (!_hookedTriggered && seconds > _random.NextDouble() * 3.0 + 1.5)
            _hookedTriggered = true;

        double tension = 0.0;
        if (_hookedTriggered && !_caught)
        {
            tension = 0.35 + (Math.Sin(seconds * 3.2) + 1.0) * 0.25 + _random.NextDouble() * 0.10;
            tension = Math.Clamp(tension, 0.0, 1.0);
        }

        if (_hookedTriggered && seconds > 8.0)
            _caught = true;

        return new FishContext
        {
            IsHooked = _hookedTriggered,
            CatchCompleted = _caught,
            Tension = tension,
            Timestamp = DateTime.Now
        };
    }

    public void ResetCycle()
    {
        _cycleStart = DateTime.Now;
        _hookedTriggered = false;
        _caught = false;
    }
}
