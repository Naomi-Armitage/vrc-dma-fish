using VrcDmaFish.Inputs;
using VrcDmaFish.Models;
using VrcDmaFish.Providers;

namespace VrcDmaFish.Core;

public sealed class FishingBot
{
    private readonly IFishSignalSource _src;
    private readonly IInputController _in;
    private readonly BotConfig _cfg;
    private DateTime _stateSince = DateTime.UtcNow;
    private bool _reelPausedForTension;

    public FishingBot(IFishSignalSource source, IInputController input, BotConfig config)
    {
        _src = source;
        _in = input;
        _cfg = config;
    }

    public FishState State { get; private set; } = FishState.Idle;

    public FishContext LastContext { get; private set; } = new();

    public void Tick()
    {
        LastContext = _src.Read();

        switch (State)
        {
            case FishState.Idle:
                _reelPausedForTension = false;
                _in.BeginCast();
                TransitionTo(FishState.Casting);
                break;

            case FishState.Casting:
                if (ElapsedMilliseconds >= _cfg.CastDurationMs)
                {
                    _in.EndCast();
                    TransitionTo(FishState.Waiting);
                }

                break;

            case FishState.Waiting:
                if (LastContext.IsHooked)
                {
                    _reelPausedForTension = false;
                    TransitionTo(FishState.Hooked);
                }

                break;

            case FishState.Hooked:
                TransitionTo(FishState.Reeling);
                break;

            case FishState.Reeling:
                if (LastContext.CatchCompleted)
                {
                    _reelPausedForTension = false;
                    TransitionTo(FishState.Cooldown);
                }
                else if (ShouldPauseReeling(LastContext.Tension))
                {
                    _reelPausedForTension = true;
                    _in.Wait(_cfg.ReelRestMs);
                }
                else
                {
                    _reelPausedForTension = false;
                    _in.ReelPulse(_cfg.ReelPulseMs);
                }

                break;

            case FishState.Cooldown:
                if (ElapsedMilliseconds >= _cfg.CooldownMs)
                {
                    _reelPausedForTension = false;
                    _src.ResetCycle();
                    TransitionTo(FishState.Idle);
                }

                break;
        }
    }

    private double ElapsedMilliseconds => (DateTime.UtcNow - _stateSince).TotalMilliseconds;

    private bool ShouldPauseReeling(float tension)
    {
        if (_reelPausedForTension)
        {
            return tension > _cfg.ReelTensionResumeThreshold;
        }

        return tension >= _cfg.ReelTensionPauseThreshold;
    }

    private void TransitionTo(FishState nextState)
    {
        State = nextState;
        _stateSince = DateTime.UtcNow;
    }
}
