using System.Threading;
using System.Threading.Tasks;

namespace Vision.Core;

public interface IGapDetector
{
    Task<GapResult> ComputeAsync(Frame left, Frame right, CancellationToken ct);
}

// Per-side detekce (Left/Right samostatně)
public interface ISideGapDetector
{
    Task<SideGapResult> ComputeAsync(Frame frame, CancellationToken ct);
}

public interface ILeftGapDetector : ISideGapDetector { }
public interface IRightGapDetector : ISideGapDetector { }

// Starší controller (BasicController.cs to používá – necháváme kvůli kompilaci)
public interface IController
{
    ControlAction Compute(GapResult gap, SystemSnapshot snapshot);
}
