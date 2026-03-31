using VrcDmaFish.Models;
using VrcDmaFish.Providers;
using VrcDmaFish.Inputs;
using VrcDmaFish.UI;
namespace VrcDmaFish.Core;
public sealed class FishingBot {
    private readonly IFishSignalSource _src;
    private readonly IInputController _in;
    private readonly BotConfig _cfg;
    public FishState State { get; private set; } = FishState.Idle;
    public FishContext LastContext { get; private set; } = new();
    private DateTime _ts = DateTime.Now;
    public FishingBot(IFishSignalSource s, IInputController i, BotConfig c) { _src = s; _in = i; _cfg = c; }
    public void Tick() {
        LastContext = _src.Read();
        if (State == FishState.Idle) { _in.BeginCast(); State = FishState.Casting; _ts = DateTime.Now; }
        else if (State == FishState.Casting && (DateTime.Now - _ts).TotalMilliseconds > _cfg.CastDurationMs) { _in.EndCast(); State = FishState.Waiting; }
        else if (State == FishState.Waiting && LastContext.IsHooked) { State = FishState.Reeling; }
        else if (State == FishState.Reeling) {
             if (LastContext.CatchCompleted) { State = FishState.Cooldown; _ts = DateTime.Now; }
             else if (LastContext.Tension < _cfg.ReelTensionPauseThreshold) _in.ReelPulse(_cfg.ReelPulseMs);
             else _in.Wait(_cfg.ReelRestMs);
        }
        else if (State == FishState.Cooldown && (DateTime.Now - _ts).TotalMilliseconds > _cfg.CooldownMs) { _src.ResetCycle(); State = FishState.Idle; }
    }
}
