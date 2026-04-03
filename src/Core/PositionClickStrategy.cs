using VrcDmaFish.Models;

namespace VrcDmaFish.Core;

public sealed class PositionClickStrategy
{
    private float? _previousBarCenterY;
    private DateTime? _previousSampleAt;
    private double _barVelocity;
    private float? _lastFishCenterY;

    public void Reset()
    {
        _previousBarCenterY = null;
        _previousSampleAt = null;
        _barVelocity = 0d;
        _lastFishCenterY = null;
    }

    public ReelDecision Decide(FishContext context, BotConfig config)
    {
        if (context.BarCenterY is not float barCenterY ||
            context.BarHeight is not float barHeight ||
            barHeight < 8f)
        {
            return new ReelDecision(false, 0);
        }

        UpdateVelocity(barCenterY, config.PositionVelocitySmooth);

        if (context.FishCenterY.HasValue)
        {
            _lastFishCenterY = context.FishCenterY.Value;
        }

        if (_lastFishCenterY is not float fishCenterY)
        {
            return new ReelDecision(false, 0);
        }

        var barTop = barCenterY - (barHeight / 2f);
        var fishInBar = (fishCenterY - barTop) / barHeight;
        var error = 0.5f - fishInBar;
        var deadZone = Math.Clamp(config.PositionDeadZoneRatio, 0f, 1f) * 0.5f;
        var direction = config.PositionPressMovesUp ? 1d : -1d;
        var normalizedVelocity = Math.Clamp(_barVelocity / barHeight, -5d, 5d);

        double holdMs;
        if (Math.Abs(error) <= deadZone)
        {
            holdMs = config.PositionBaseHoldMs;
        }
        else
        {
            holdMs = config.PositionBaseHoldMs
                + (direction * error * config.PositionHoldGainMs)
                + (direction * normalizedVelocity * config.PositionVelocityDampingMs);
        }

        holdMs = Math.Clamp(holdMs, config.ReelHoldMinMs, config.ReelHoldMaxMs);

        return holdMs > config.ReelHoldMinMs
            ? new ReelDecision(true, (int)Math.Round(holdMs))
            : new ReelDecision(false, 0);
    }

    private void UpdateVelocity(float barCenterY, double smoothFactor)
    {
        var now = DateTime.UtcNow;
        if (_previousBarCenterY.HasValue && _previousSampleAt.HasValue)
        {
            var elapsedSeconds = (now - _previousSampleAt.Value).TotalSeconds;
            if (elapsedSeconds > 0.003d)
            {
                var rawVelocity = (barCenterY - _previousBarCenterY.Value) / elapsedSeconds;
                var smoothing = Math.Clamp(smoothFactor, 0d, 0.95d);
                _barVelocity = (smoothing * _barVelocity) + ((1d - smoothing) * rawVelocity);
            }
        }

        _previousBarCenterY = barCenterY;
        _previousSampleAt = now;
    }
}
