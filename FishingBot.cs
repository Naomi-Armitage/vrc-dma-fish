namespace VrcDmaFish;

public sealed class FishingBot
{
    private readonly IFishSignalSource _signalSource;
    private readonly IInputController _input;
    private readonly BotConfig _config;

    private FishState _state = FishState.Idle;
    private DateTime _stateEnteredAt = DateTime.Now;
    private bool _pausedForTension;

    public FishingBot(IFishSignalSource signalSource, IInputController input, BotConfig config)
    {
        _signalSource = signalSource;
        _input = input;
        _config = config;
    }

    public void Tick()
    {
        var ctx = _signalSource.Read();

        switch (_state)
        {
            case FishState.Idle:
                Transition(FishState.Casting, "starting cast cycle");
                _input.BeginCast();
                break;

            case FishState.Casting:
                if (ElapsedMs() >= _config.CastDurationMs)
                {
                    _input.EndCast();
                    Transition(FishState.Waiting, "cast completed, waiting for fish");
                }
                break;

            case FishState.Waiting:
                if (ctx.IsHooked)
                    Transition(FishState.Hooked, $"fish hooked ({ctx})");
                break;

            case FishState.Hooked:
                _pausedForTension = false;
                Transition(FishState.Reeling, "start reeling");
                break;

            case FishState.Reeling:
                if (ctx.CatchCompleted)
                {
                    Logger.Info("BOT", $"Catch completed ({ctx})");
                    Transition(FishState.Cooldown, "enter cooldown");
                    break;
                }

                if (!_pausedForTension && ctx.Tension >= _config.ReelTensionPauseThreshold)
                {
                    _pausedForTension = true;
                    Logger.Warn("BOT", $"Tension high, pause reeling ({ctx.Tension:F2})");
                }
                else if (_pausedForTension && ctx.Tension <= _config.ReelTensionResumeThreshold)
                {
                    _pausedForTension = false;
                    Logger.Info("BOT", $"Tension normalized, resume reeling ({ctx.Tension:F2})");
                }

                if (_pausedForTension)
                    _input.Wait(_config.ReelRestMs);
                else
                    _input.ReelPulse(_config.ReelPulseMs);
                break;

            case FishState.Cooldown:
                if (ElapsedMs() >= _config.CooldownMs)
                {
                    _signalSource.ResetCycle();
                    Transition(FishState.Idle, "cooldown finished");
                }
                break;
        }
    }

    private void Transition(FishState newState, string reason)
    {
        Logger.Info("FSM", $"{_state} -> {newState} | {reason}");
        _state = newState;
        _stateEnteredAt = DateTime.Now;
    }

    private long ElapsedMs() => (long)(DateTime.Now - _stateEnteredAt).TotalMilliseconds;
}
