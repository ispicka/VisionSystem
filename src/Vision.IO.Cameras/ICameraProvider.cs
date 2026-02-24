using System.Threading;
using System.Threading.Tasks;
using Vision.Core;

namespace Vision.IO.Cameras;

public interface ICameraProvider
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Frame? TryGetLatest(CameraId camera);
}
