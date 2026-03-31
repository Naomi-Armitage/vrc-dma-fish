namespace VrcDmaFish;

public sealed class FishingBot
{
    private readonly IFishSignalSource _signalSource;
    private readonly IInputController _input;
    private readonly BotConfig _config;

    public FishState State { get; private set; } = FishState.Idle;
    public FishContext LastContext { get; private set; } = new();
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
        LastContext = _signalSource.Read();

        switch (State)
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
                if (LastContext.IsHooked)
                    Transition(FishState.Hooked, "fish hooked!");
                break;

            case FishState.Hooked:
                _pausedForTension = false;
                Transition(FishState.Reeling, "start reeling");
                break;

            case FishState.Reeling:
                if (LastContext.CatchCompleted)
                {
                    Transition(FishState.Cooldown, "catch completed");
                    break;
                }

                if (!_pausedForTension && LastContext.Tension >= _config.ReelTensionPauseThreshold)
                {
                    _pausedForTension = true;
                    Logger.Warn("BOT", "Tension high! pausing...");
                }
                else if (_pausedForTension && LastContext.Tension <= _config.ReelTensionResumeThreshold)
                {
                    _pausedForTension = false;
                    Logger.Info("BOT", "Tension normalized, resume...");
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
        Logger.Info("FSM", $"{State} -> {newState} | {reason}");
        State = newState;
        _stateEnteredAt = DateTime.Now;
    }

    private long ElapsedMs() => (long)(DateTime.Now - _stateEnteredAt).TotalMilliseconds;
}
