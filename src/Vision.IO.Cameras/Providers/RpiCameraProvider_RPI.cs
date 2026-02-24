using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Vision.Core;

namespace Vision.IO.Cameras.Providers;

#if RPI
public sealed class RpiCameraProvider : Vision.IO.Cameras.ICameraProvider
{
    private readonly ConcurrentDictionary<CameraId, Frame?> _latest = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => Loop(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { if (_loop is not null) await _loop; } catch { }
        _cts.Dispose();
        _cts = null;
    }

    public Frame? TryGetLatest(CameraId camera)
        => _latest.TryGetValue(camera, out var f) ? f : null;

    private void Loop(CancellationToken ct)
    {
        // TODO: implement real capture (libcamera/RTSP/SDK)
        while (!ct.IsCancellationRequested)
        {
            Thread.Sleep(200);
        }
    }
}
#endif
