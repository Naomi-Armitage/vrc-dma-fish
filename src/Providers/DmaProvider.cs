// using vmmsharp; // 请在您的 DMA 环境中手动重新引入 NuGet 包喵！
using VrcDmaFish.Models;
using VrcDmaFish.UI;
namespace VrcDmaFish.Providers;
public sealed class DmaProvider : IFishSignalSource, IDisposable {
    // private Vmm? _vmm; 
    public DmaProvider(string proc) { Logger.Info("DMA", "DMA Provider 模板已加载。请在 DMA 攻击机环境内补全 vmmsharp 逻辑喵！"); }
    public FishContext Read() => new FishContext();
    public void ResetCycle() {}
    public void Dispose() { /* _vmm?.Dispose(); */ }
}
