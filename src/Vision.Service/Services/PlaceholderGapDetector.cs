using System.Threading;
using System.Threading.Tasks;
using Vision.Core;

namespace Vision.Service.Services;

public sealed class PlaceholderLeftGapDetector : ILeftGapDetector
{
    public Task<SideGapResult> ComputeAsync(Frame frame, CancellationToken ct)
    {
        return Task.FromResult(new SideGapResult(
            Timestamp: frame.Timestamp,
            Side: SideId.Left,
            GapMm: 2.00,
            Quality: 1.0,
            Diagnostic: "placeholder-left"
        ));
    }
}

public sealed class PlaceholderRightGapDetector : IRightGapDetector
{
    public Task<SideGapResult> ComputeAsync(Frame frame, CancellationToken ct)
    {
        return Task.FromResult(new SideGapResult(
            Timestamp: frame.Timestamp,
            Side: SideId.Right,
            GapMm: 2.10,
            Quality: 1.0,
            Diagnostic: "placeholder-right"
        ));
    }
}
