namespace VrcDmaFish;

public interface IInputController
{
    void BeginCast();
    void EndCast();
    void ReelPulse(int durationMs);
    void Wait(int durationMs);
}
