namespace VrcDmaFish;

public interface IFishSignalSource
{
    FishContext Read();
    void ResetCycle();
}
