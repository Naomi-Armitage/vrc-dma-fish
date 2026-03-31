using VrcDmaFish.UI;

namespace VrcDmaFish.Inputs;

public sealed class ConsoleInputController : IInputController
{
    public void BeginCast() => Logger.Info("INPUT", "Begin cast");
    public void EndCast() => Logger.Info("INPUT", "End cast");
    public void ReelPulse(int ms) => Logger.Info("INPUT", $"Pulse {ms}ms");
    public void Wait(int ms) => Logger.Info("INPUT", $"Wait {ms}ms");
    public void Dispose()
    {
    }
}
