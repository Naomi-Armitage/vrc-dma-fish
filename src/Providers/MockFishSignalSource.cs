using VrcDmaFish.Models;
namespace VrcDmaFish.Providers;
public sealed class MockFishSignalSource : IFishSignalSource {
    private float _tension = 0f;
    private bool _hooked = false;
    private DateTime _start = DateTime.Now;

    public FishContext Read() {
        var elapsed = (DateTime.Now - _start).TotalSeconds;
        if (elapsed > 5 && !_hooked) _hooked = true;
        if (_hooked) _tension = Math.Min(1.0f, _tension + 0.05f);
        return new FishContext { IsHooked = _hooked, Tension = _tension, CatchCompleted = _tension >= 1.0f };
    }
    public void ResetCycle() { _tension = 0f; _hooked = false; _start = DateTime.Now; }
}
