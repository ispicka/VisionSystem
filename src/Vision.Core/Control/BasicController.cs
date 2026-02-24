using System;

namespace Vision.Core.Control;

public sealed class BasicController : Vision.Core.IController
{
    private readonly double _targetLeft;
    private readonly double _targetRight;
    private readonly double _deadband;

    public BasicController(double targetLeftMm = 2.0, double targetRightMm = 2.0, double deadbandMm = 0.2)
    {
        _targetLeft = targetLeftMm;
        _targetRight = targetRightMm;
        _deadband = deadbandMm;
    }

    public Vision.Core.ControlAction Compute(Vision.Core.GapResult gap, Vision.Core.SystemSnapshot snapshot)
    {
        if (gap.Quality < 0.6)
            return new(gap.Timestamp, Vision.Core.StepAction.None, 0, "Quality low");

        var dl = gap.LeftGapMm - _targetLeft;
        var dr = gap.RightGapMm - _targetRight;

        if (Math.Abs(dl) < _deadband && Math.Abs(dr) < _deadband)
            return new(gap.Timestamp, Vision.Core.StepAction.None, 0, "Within deadband");

        if (Math.Abs(dl) >= Math.Abs(dr))
        {
            if (dl > _deadband)  return new(gap.Timestamp, Vision.Core.StepAction.LeftMinus, 1, "Left gap too big");
            if (dl < -_deadband) return new(gap.Timestamp, Vision.Core.StepAction.LeftPlus, 1, "Left gap too small");
        }
        else
        {
            if (dr > _deadband)  return new(gap.Timestamp, Vision.Core.StepAction.RightMinus, 1, "Right gap too big");
            if (dr < -_deadband) return new(gap.Timestamp, Vision.Core.StepAction.RightPlus, 1, "Right gap too small");
        }

        return new(gap.Timestamp, Vision.Core.StepAction.None, 0, "No rule matched");
    }
}
