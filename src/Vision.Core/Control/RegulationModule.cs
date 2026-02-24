using System;

namespace Vision.Core.Control;

/// <summary>
/// Per-side regulation logic (2 instances: Left/Right).
/// Produces step commands with hysteresis + cooldown.
/// </summary>
public sealed class RegulationModule
{
    public sealed record Params(
        SideId Side,
        double TargetGapMm,
        double DeadbandMm,
        double HysteresisMm,
        double MinQuality,
        int CooldownMs,
        int MaxStepsPerAction
    );

    private readonly Params _p;

    // Simple internal state
    private DateTimeOffset _lastStepAt = DateTimeOffset.MinValue;
    private bool _wasOutside = false;

    public RegulationModule(Params p) => _p = p;

    public StepCommand? Decide(SideGapResult gap, ControlMode mode)
    {
        if (mode != ControlMode.Auto) return null;

        if (gap.Quality < _p.MinQuality)
        {
            _wasOutside = false;
            return null;
        }

        var err = gap.GapMm - _p.TargetGapMm;
        var absErr = Math.Abs(err);

        // Deadband + hysteresis: need to exceed Deadband to trigger,
        // then must come back below (Deadband - Hysteresis) to re-arm.
        var armThreshold = _p.DeadbandMm;
        var rearmThreshold = Math.Max(0.0, _p.DeadbandMm - _p.HysteresisMm);

        if (!_wasOutside)
        {
            if (absErr < armThreshold) return null;
            _wasOutside = true; // armed/triggered
        }
        else
        {
            if (absErr > rearmThreshold)
            {
                // still outside, but we still allow repeated steps based on cooldown
            }
            else
            {
                _wasOutside = false; // re-armed (back in band)
                return null;
            }
        }

        // Cooldown
        var now = gap.Timestamp;
        if ((now - _lastStepAt).TotalMilliseconds < _p.CooldownMs) return null;

        // Direction: if gap too big -> move minus; if too small -> plus.
        var action = err > 0
            ? (_p.Side == SideId.Left ? StepAction.LeftMinus : StepAction.RightMinus)
            : (_p.Side == SideId.Left ? StepAction.LeftPlus : StepAction.RightPlus);

        _lastStepAt = now;

        return new StepCommand(now, _p.Side, action, Steps: Math.Clamp(1, 1, _p.MaxStepsPerAction),
            Reason: $"err={err:F3}mm target={_p.TargetGapMm:F3}mm");
    }
}
