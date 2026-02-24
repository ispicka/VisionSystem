using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vision.Core;

namespace Vision.IO.Cameras.Providers;

#if PC
public sealed class FileFolderCameraProvider : Vision.IO.Cameras.ICameraProvider
{
    private readonly string _root;
    private readonly ConcurrentDictionary<CameraId, Frame?> _latest = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public FileFolderCameraProvider(string rootFolder) => _root = rootFolder;

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
        var leftDir = Path.Combine(_root, "left");
        var rightDir = Path.Combine(_root, "right");

        var leftFiles = Directory.Exists(leftDir) ? Directory.GetFiles(leftDir) : Array.Empty<string>();
        var rightFiles = Directory.Exists(rightDir) ? Directory.GetFiles(rightDir) : Array.Empty<string>();

        int i = 0;
        while (!ct.IsCancellationRequested)
        {
            if (leftFiles.Length > 0)
                _latest[CameraId.Left] = LoadAsFakeBgr(leftFiles[i % leftFiles.Length], CameraId.Left);

            if (rightFiles.Length > 0)
                _latest[CameraId.Right] = LoadAsFakeBgr(rightFiles[i % rightFiles.Length], CameraId.Right);

            i++;
            Thread.Sleep(100);
        }
    }

    private static Frame LoadAsFakeBgr(string path, CameraId id)
    {
        var bytes = File.ReadAllBytes(path);
        return new Frame(id, DateTimeOffset.UtcNow, bytes, 0, 0, 0);
    }
}
#endif
