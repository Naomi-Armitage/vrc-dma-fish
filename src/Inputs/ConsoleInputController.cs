using VrcDmaFish.UI;

namespace VrcDmaFish.Inputs;

public sealed class ConsoleInputController : IInputController
{
    public void BeginCast() => Logger.Info("输入", "开始按下抛竿。");
    public void EndCast() => Logger.Info("输入", "结束抛竿，松开鼠标。");
    public void Click(int ms) => Logger.Info("输入", $"点击鼠标左键 {ms}ms。");
    public void ReelPulse(int ms) => Logger.Info("输入", $"按住收线 {ms}ms。");
    public void ReleaseReel() => Logger.Info("输入", "松开收线。");
    public void Wait(int ms) => Logger.Info("输入", $"等待 {ms}ms。");
    public void Dispose()
    {
    }
}
