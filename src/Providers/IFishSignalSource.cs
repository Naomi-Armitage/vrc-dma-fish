using VrcDmaFish.Models;
namespace VrcDmaFish.Providers;
public interface IFishSignalSource {
    FishContext Read();
    void ResetCycle();
}
