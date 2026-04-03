using VrcDmaFish.Models;

namespace VrcDmaFish.Core;

public readonly record struct ReelDecision(bool ShouldPress, int HoldMs);

public sealed class TensionClickStrategy
{
    private float? _previousTension;
    private DateTime? _previousSampleAt;
    private float _smoothedVelocity;

    public void Reset()
    {
        _previousTension = null;
        _previousSampleAt = null;
        _smoothedVelocity = 0f;
    }

    public ReelDecision Decide(FishContext context, BotConfig config)
    {
        UpdateVelocity(context.Tension);

        var targetTension = (config.ReelTensionPauseThreshold + config.ReelTensionResumeThreshold) / 2f;
        var error = Math.Clamp(targetTension - context.Tension, -1f, 1f);
        var holdMs = config.ReelPulseMs
            + (int)Math.Round(error * config.ReelHoldGainMs)
            - (int)Math.Round(_smoothedVelocity * config.ReelVelocityDampingMs);

        holdMs = Math.Clamp(holdMs, 0, config.ReelHoldMaxMs);
        return holdMs >= config.ReelHoldMinMs
            ? new ReelDecision(true, holdMs)
            : new ReelDecision(false, 0);
    }

    private void UpdateVelocity(float tension)
    {
        var now = DateTime.UtcNow;
        if (_previousTension.HasValue && _previousSampleAt.HasValue)
        {
            var elapsedSeconds = (float)(now - _previousSampleAt.Value).TotalSeconds;
            if (elapsedSeconds > 0.003f)
            {
                var rawVelocity = (tension - _previousTension.Value) / elapsedSeconds;
                _smoothedVelocity = 0.5f * _smoothedVelocity + 0.5f * rawVelocity;
            }
        }

        _previousTension = tension;
        _previousSampleAt = now;
    }
}
