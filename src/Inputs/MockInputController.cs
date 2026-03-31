namespace VrcDmaFish.Inputs;

public sealed class MockInputController : IInputController
{
    public void BeginCast() { }
    public void EndCast() { }
    public void ReelPulse(int ms) { }
    public void Wait(int ms) { }
    public void ClickLeft() { }
    public void Move(int x, int y) { }
    public void Dispose() { }
}
