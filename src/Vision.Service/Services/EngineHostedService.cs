using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vision.Core;
using Vision.Core.Control;
using Vision.IO.Cameras;
using Vision.IO.Plc;
using Vision.Service.State;

namespace Vision.Service.Services;

public sealed class EngineHostedService : BackgroundService
{
    private readonly ILogger<EngineHostedService> _log;
    private readonly AppState _state;
    private readonly IPlcClient _plc;
    private readonly ICameraProvider _cams;

    private readonly ILeftGapDetector _detLeft;
    private readonly IRightGapDetector _detRight;

    private readonly RegulationModule _regLeft;
    private readonly RegulationModule _regRight;

    private Frame? _lastLeft;
    private Frame? _lastRight;
    private DateTimeOffset _lastLeftSeen = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRightSeen = DateTimeOffset.MinValue;

    private DateTimeOffset _lastComputeAt = DateTimeOffset.MinValue;

    private SideGapResult? _lastLeftRes;
    private SideGapResult? _lastRightRes;

    // Throttling / change detection for COMPOSED log
    private DateTimeOffset _lastComposedLogAt = DateTimeOffset.MinValue;
    private double _lastLogL = double.NaN;
    private double _lastLogR = double.NaN;
    private double _lastLogQ = double.NaN;

    public EngineHostedService(
        ILogger<EngineHostedService> log,
        AppState state,
        IPlcClient plc,
        ICameraProvider cams,
        ILeftGapDetector detLeft,
        IRightGapDetector detRight)
    {
        _log = log;
        _state = state;
        _plc = plc;
        _cams = cams;
        _detLeft = detLeft;
        _detRight = detRight;

        _regLeft = new RegulationModule(new RegulationModule.Params(
            Side: SideId.Left,
            TargetGapMm: 2.0,
            DeadbandMm: 0.25,
            HysteresisMm: 0.05,
            MinQuality: 0.6,
            CooldownMs: 250,
            MaxStepsPerAction: 1
        ));

        _regRight = new RegulationModule(new RegulationModule.Params(
            Side: SideId.Right,
            TargetGapMm: 2.0,
            DeadbandMm: 0.25,
            HysteresisMm: 0.05,
            MinQuality: 0.6,
            CooldownMs: 250,
            MaxStepsPerAction: 1
        ));
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _state.AddMsg("Orchestrator starting...");

        // IMPORTANT: startup must survive without PLC/cameras
        try { await _plc.ConnectAsync(cancellationToken); }
        catch (Exception ex) { _state.AddMsg("PLC connect failed: " + ex.Message); }

        try { await _cams.StartAsync(cancellationToken); }
        catch (Exception ex) { _state.AddMsg("Cameras start failed: " + ex.Message); }

        await base.StartAsync(cancellationToken);
        _state.AddMsg("Orchestrator started.");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var plcPollDelay = TimeSpan.FromMilliseconds(50);   // 20 Hz
        var visionPeriod = TimeSpan.FromMilliseconds(500);  // 2 Hz (produkce)
        var frameTimeout = TimeSpan.FromSeconds(2);

        while (!ct.IsCancellationRequested)
        {
            // Track whether anything new was computed in THIS loop
            bool leftUpdated = false;
            bool rightUpdated = false;
            bool periodicUpdated = false;

            // UI commands
            if (_state.TryDequeueResetRequest())
            {
                try { await _plc.ResetHandshakeAsync(ct); _state.AddMsg("Handshake reset."); }
                catch (Exception ex) { _state.AddMsg("ResetHandshake error: " + ex.Message); }
            }

            if (_state.TryDequeueManualStep(out var manual))
            {
                try
                {
                    var ok = await _plc.ExecuteStepAsync(manual.Action, 2000, ct);
                    _state.AddMsg(ok ? $"MANUAL {manual.Action}" : $"MANUAL {manual.Action} FAILED");
                }
                catch (Exception ex)
                {
                    _state.AddMsg("Manual step error: " + ex.Message);
                }
            }

            // PLC status poll
            _state.Plc = await _plc.ReadStatusAsync(ct);

            // Acquire frames (test first)
            bool testLeftArrived = _state.TryConsumeTestLeft(out var testLeft) && testLeft is not null;
            bool testRightArrived = _state.TryConsumeTestRight(out var testRight) && testRight is not null;

            var now = DateTimeOffset.UtcNow;

            if (testLeftArrived)
            {
                _lastLeft = testLeft!;
                _lastLeftSeen = _lastLeft.Timestamp;
            }
            else
            {
                var left = _cams.TryGetLatest(CameraId.Left);
                if (left is not null)
                {
                    _lastLeft = left;
                    _lastLeftSeen = left.Timestamp;
                }
            }

            if (testRightArrived)
            {
                _lastRight = testRight!;
                _lastRightSeen = _lastRight.Timestamp;
            }
            else
            {
                var right = _cams.TryGetLatest(CameraId.Right);
                if (right is not null)
                {
                    _lastRight = right;
                    _lastRightSeen = right.Timestamp;
                }
            }

            // Timeout supervision
            bool leftOk = _lastLeft is not null && (now - _lastLeftSeen) <= frameTimeout;
            bool rightOk = _lastRight is not null && (now - _lastRightSeen) <= frameTimeout;

            _state.CamLeft = new CameraStatus(leftOk, 0, _lastLeftSeen, 0);
            _state.CamRight = new CameraStatus(rightOk, 0, _lastRightSeen, 0);

            // Immediate compute on test arrival (per side)
            if (testLeftArrived && leftOk && _lastLeft is not null)
            {
                try
                {
                    _lastLeftRes = await _detLeft.ComputeAsync(_lastLeft, ct);
                    leftUpdated = true;
                    _state.AddMsg($"TEST LEFT computed: gap={_lastLeftRes.GapMm:F2} q={_lastLeftRes.Quality:F2}");
                }
                catch (Exception ex)
                {
                    _state.AddMsg("TEST LEFT compute error: " + ex.Message);
                }
            }

            if (testRightArrived && rightOk && _lastRight is not null)
            {
                try
                {
                    _lastRightRes = await _detRight.ComputeAsync(_lastRight, ct);
                    rightUpdated = true;
                    _state.AddMsg($"TEST RIGHT computed: gap={_lastRightRes.GapMm:F2} q={_lastRightRes.Quality:F2}");
                }
                catch (Exception ex)
                {
                    _state.AddMsg("TEST RIGHT compute error: " + ex.Message);
                }
            }

            // Periodic compute (production behavior)
            bool timeDue = (now - _lastComputeAt) >= visionPeriod;
            if (timeDue && leftOk && rightOk && _lastLeft is not null && _lastRight is not null)
            {
                _lastComputeAt = now;

                try
                {
                    _lastLeftRes = await _detLeft.ComputeAsync(_lastLeft, ct);
                    _lastRightRes = await _detRight.ComputeAsync(_lastRight, ct);
                    periodicUpdated = true;
                }
                catch (Exception ex)
                {
                    _state.AddMsg("Vision compute error: " + ex.Message);
                }
            }

            // Compose ONLY if something new happened in this loop (or periodic compute)
            bool shouldCompose = (leftUpdated || rightUpdated || periodicUpdated);

            if (shouldCompose && _lastLeftRes is not null && _lastRightRes is not null)
            {
                var q = Math.Min(_lastLeftRes.Quality, _lastRightRes.Quality);

                _state.LastGap = new GapResult(
                    Timestamp: now,
                    LeftGapMm: _lastLeftRes.GapMm,
                    RightGapMm: _lastRightRes.GapMm,
                    Quality: q,
                    Diagnostic: $"{_lastLeftRes.Diagnostic} | {_lastRightRes.Diagnostic}"
                );

                // Throttled COMPOSED log: on change OR at most once per second
                bool changed =
                    !NearlyEqual(_lastLogL, _lastLeftRes.GapMm, 1e-6) ||
                    !NearlyEqual(_lastLogR, _lastRightRes.GapMm, 1e-6) ||
                    !NearlyEqual(_lastLogQ, q, 1e-6);

                bool timeToLog = (now - _lastComposedLogAt) >= TimeSpan.FromSeconds(1);

                if (changed || timeToLog)
                {
                    _lastComposedLogAt = now;
                    _lastLogL = _lastLeftRes.GapMm;
                    _lastLogR = _lastRightRes.GapMm;
                    _lastLogQ = q;

                    _state.AddMsg($"COMPOSED gap: L={_lastLeftRes.GapMm:F2} R={_lastRightRes.GapMm:F2} Q={q:F2}");
                }

                // Regulation (only meaningful in Auto)
                var cmdL = _regLeft.Decide(_lastLeftRes, _state.Mode);
                var cmdR = _regRight.Decide(_lastRightRes, _state.Mode);
                var cmd = Choose(cmdL, cmdR, _lastLeftRes, _lastRightRes);

                if (cmd is not null)
                {
                    var ok = await _plc.ExecuteStepAsync(cmd.Action, 2000, ct);
                    _state.LastAction = new ControlAction(cmd.Timestamp, cmd.Action, cmd.Steps, cmd.Reason);
                    _state.AddMsg(ok ? $"AUTO {cmd.Action}" : $"AUTO {cmd.Action} FAILED");
                }
                else
                {
                    _state.LastAction = new ControlAction(now, StepAction.None, 0, "No action");
                }
            }

            await Task.Delay(plcPollDelay, ct);
        }
    }

    private static bool NearlyEqual(double a, double b, double eps)
        => double.IsNaN(a) ? false : Math.Abs(a - b) <= eps;

    private static StepCommand? Choose(StepCommand? left, StepCommand? right, SideGapResult rL, SideGapResult rR)
    {
        if (left is null) return right;
        if (right is null) return left;

        var dl = Math.Abs(rL.GapMm - 2.0);
        var dr = Math.Abs(rR.GapMm - 2.0);
        return dl >= dr ? left : right;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _state.AddMsg("Orchestrator stopping...");
        try { await _cams.StopAsync(cancellationToken); } catch { }
        try { await _plc.DisconnectAsync(cancellationToken); } catch { }
        _state.AddMsg("Orchestrator stopped.");
        await base.StopAsync(cancellationToken);
    }
}
