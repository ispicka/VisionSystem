using System.Threading;
using System.Threading.Tasks;
using Vision.Core;

namespace Vision.IO.Plc;

public interface IPlcClient
{
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);

    Task<PlcStatus> ReadStatusAsync(CancellationToken ct);

    Task ResetHandshakeAsync(CancellationToken ct);

    Task<bool> ExecuteStepAsync(StepAction action, int overallTimeoutMs, CancellationToken ct);
}
