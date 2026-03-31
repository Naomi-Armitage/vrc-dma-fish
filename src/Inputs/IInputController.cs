namespace VrcDmaFish.Inputs;

public interface IInputController : IDisposable
{
    void BeginCast();
    void EndCast();
    void ReelPulse(int durationMs);
    void Wait(int durationMs);
}
