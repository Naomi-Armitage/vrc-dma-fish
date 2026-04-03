using VrcDmaFish.Inputs;
using VrcDmaFish.Models;
using VrcDmaFish.Providers;
using VrcDmaFish.UI;

namespace VrcDmaFish.Core;

public sealed class FishingBot
{
    private readonly IFishSignalSource _src;
    private readonly IInputController _in;
    private readonly BotConfig _cfg;
    private readonly TensionClickStrategy _tensionStrategy = new();
    private readonly PositionClickStrategy _positionStrategy = new();
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

    public TimeSpan StateElapsed => DateTime.UtcNow - _stateSince;

    public void Tick()
    {
        LastContext = _src.Read();
        Logger.Debug(
            "状态",
            $"tick state={State} hooked={LastContext.IsHooked} catch={LastContext.CatchCompleted} tension={LastContext.Tension:0.000} fish={FormatNullable(LastContext.FishCenterY)} bar={FormatNullable(LastContext.BarCenterY)} height={FormatNullable(LastContext.BarHeight)}");

        switch (State)
        {
            case FishState.Idle:
                _reelPausedForTension = false;
                _tensionStrategy.Reset();
                _positionStrategy.Reset();
                _in.ReleaseReel();
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
                _in.ReleaseReel();
                if (LastContext.IsHooked)
                {
                    _reelPausedForTension = false;
                    TransitionTo(FishState.Hooked);
                }

                break;

            case FishState.Hooked:
                _in.Click(_cfg.HookClickMs);
                _tensionStrategy.Reset();
                _positionStrategy.Reset();
                TransitionTo(FishState.Reeling);
                break;

            case FishState.Reeling:
                if (LastContext.CatchCompleted)
                {
                    _reelPausedForTension = false;
                    _in.ReleaseReel();
                    TransitionTo(FishState.Cooldown);
                }
                else if (HasPositionSignal())
                {
                    var decision = _positionStrategy.Decide(LastContext, _cfg);
                    Logger.Debug("控制", $"位置控制 decision press={decision.ShouldPress} hold={decision.HoldMs}");
                    if (decision.ShouldPress)
                    {
                        _in.ReelPulse(decision.HoldMs);
                    }
                    else
                    {
                        _in.ReleaseReel();
                        _in.Wait(_cfg.ReelRestMs);
                    }
                }
                else if (ShouldPauseReeling(LastContext.Tension))
                {
                    _reelPausedForTension = true;
                    Logger.Debug("控制", $"张力过高，暂停收线 tension={LastContext.Tension:0.000}");
                    _in.ReleaseReel();
                    _in.Wait(_cfg.ReelRestMs);
                }
                else
                {
                    _reelPausedForTension = false;
                    var decision = _tensionStrategy.Decide(LastContext, _cfg);
                    Logger.Debug("控制", $"张力控制 decision press={decision.ShouldPress} hold={decision.HoldMs}");
                    if (decision.ShouldPress)
                    {
                        _in.ReelPulse(decision.HoldMs);
                    }
                    else
                    {
                        _in.ReleaseReel();
                        _in.Wait(_cfg.ReelRestMs);
                    }
                }

                break;

            case FishState.Cooldown:
                _in.ReleaseReel();
                if (ElapsedMilliseconds >= _cfg.CooldownMs)
                {
                    _reelPausedForTension = false;
                    _tensionStrategy.Reset();
                    _positionStrategy.Reset();
                    _src.ResetCycle();
                    TransitionTo(FishState.Idle);
                }

                break;
        }
    }

    private double ElapsedMilliseconds => (DateTime.UtcNow - _stateSince).TotalMilliseconds;

    private bool HasPositionSignal() => LastContext.HasPositionData;

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
        Logger.Debug("状态", $"状态切换 {State} -> {nextState}");
        State = nextState;
        _stateSince = DateTime.UtcNow;
    }

    private static string FormatNullable(float? value) => value.HasValue ? value.Value.ToString("0.###") : "-";
}
