namespace VrcDmaFish;

public sealed class ConsoleInputController : IInputController
{
    public void BeginCast() => Logger.Info("INPUT", "Begin cast");
    public void EndCast() => Logger.Info("INPUT", "End cast");
    public void ReelPulse(int durationMs) => Logger.Info("INPUT", $"Reel pulse {durationMs}ms");
    public void Wait(int durationMs) => Logger.Info("INPUT", $"Wait {durationMs}ms");
}
