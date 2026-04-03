namespace VrcDmaFish.Inputs;

public interface IInputController : IDisposable
{
    void BeginCast();
    void EndCast();
    void Click(int durationMs);
    void ReelPulse(int durationMs);
    void ReleaseReel();
    void Wait(int durationMs);
}
