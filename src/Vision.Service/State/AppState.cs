using System;
using System.Collections.Concurrent;
using System.Threading;
using Vision.Core;

namespace Vision.Service.State;

public sealed class AppState
{
    public volatile ControlMode Mode = ControlMode.Manual;

    public volatile PlcStatus Plc = new(false, false, false, false, false, false, false, false, DateTimeOffset.UtcNow);
    public volatile CameraStatus CamLeft = new(false, 0, DateTimeOffset.MinValue, 0);
    public volatile CameraStatus CamRight = new(false, 0, DateTimeOffset.MinValue, 0);

    public volatile GapResult? LastGap;
    public volatile ControlAction? LastAction;

    private readonly ConcurrentQueue<string> _msgs = new();

    // UI/External commands
    private readonly ConcurrentQueue<StepCommand> _manualSteps = new();
    private int _resetReq = 0;

    // ===== Test frames (file-picked) =====
    private Frame? _testLeft;
    private Frame? _testRight;
    private int _testLeftDirty = 0;
    private int _testRightDirty = 0;

    public void SetTestFrameLeft(Frame frame)
    {
        _testLeft = frame;
        Interlocked.Exchange(ref _testLeftDirty, 1);
        AddMsg($"TEST Left image loaded @ {frame.Timestamp:HH:mm:ss}");
    }

    public void SetTestFrameRight(Frame frame)
    {
        _testRight = frame;
        Interlocked.Exchange(ref _testRightDirty, 1);
        AddMsg($"TEST Right image loaded @ {frame.Timestamp:HH:mm:ss}");
    }

    public bool TryConsumeTestLeft(out Frame? frame)
    {
        if (Interlocked.Exchange(ref _testLeftDirty, 0) == 1 && _testLeft is not null)
        {
            frame = _testLeft;
            return true;
        }
        frame = null;
        return false;
    }

    public bool TryConsumeTestRight(out Frame? frame)
    {
        if (Interlocked.Exchange(ref _testRightDirty, 0) == 1 && _testRight is not null)
        {
            frame = _testRight;
            return true;
        }
        frame = null;
        return false;
    }

    public void RequestResetHandshake() => Interlocked.Exchange(ref _resetReq, 1);

    public bool TryDequeueResetRequest()
        => Interlocked.Exchange(ref _resetReq, 0) == 1;

    public void RequestManualStep(StepCommand cmd) => _manualSteps.Enqueue(cmd);

    public bool TryDequeueManualStep(out StepCommand cmd) => _manualSteps.TryDequeue(out cmd);

    public void AddMsg(string msg)
    {
        _msgs.Enqueue($"{DateTimeOffset.UtcNow:HH:mm:ss} {msg}");
        while (_msgs.Count > 10 && _msgs.TryDequeue(out _)) { }
    }

    public string[] GetMsgs() => _msgs.ToArray();

    public SystemSnapshot Snapshot()
        => new(DateTimeOffset.UtcNow, Mode, Plc, CamLeft, CamRight, LastGap, LastAction, GetMsgs());
}
