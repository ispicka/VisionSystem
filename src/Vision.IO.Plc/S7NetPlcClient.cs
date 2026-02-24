using System;
using System.Threading;
using System.Threading.Tasks;
using S7.Net;
using S7Plc = S7.Net.Plc;
using Vision.Core;
using Kern.PlcCamCorr;

namespace Vision.IO.Plc;

public sealed class S7NetPlcClient : IPlcClient, IDisposable
{
    private readonly string _ip;
    private readonly short _rack;
    private readonly short _slot;
    private readonly int _dbNumber;

    private S7Plc? _plc;
    private CamCorrDb1122_Batch? _cam;

    public S7NetPlcClient(string ip, short rack = 0, short slot = 2, int dbNumber = 1122)
    {
        _ip = ip;
        _rack = rack;
        _slot = slot;
        _dbNumber = dbNumber;
    }

    public Task ConnectAsync(CancellationToken ct)
    {
        // IMPORTANT: never throw from here (UI/host start must survive without PLC)
        try
        {
            _plc = new S7Plc(CpuType.S7300, _ip, _rack, _slot);
            _plc.Open();
            _cam = new CamCorrDb1122_Batch(_plc, _dbNumber);
        }
        catch
        {
            try { if (_plc is { IsConnected: true }) _plc.Close(); } catch { }
            _plc = null;
            _cam = null;
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct)
    {
        try { if (_plc is { IsConnected: true }) _plc.Close(); } catch { }
        _plc = null;
        _cam = null;
        return Task.CompletedTask;
    }

    public Task ResetHandshakeAsync(CancellationToken ct)
    {
        if (_plc is null || !_plc.IsConnected || _cam is null) return Task.CompletedTask;

        return Task.Run(() =>
        {
            try { _cam.PulseReset(120); } catch { }
        }, ct);
    }

    public Task<PlcStatus> ReadStatusAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (_plc is null || !_plc.IsConnected || _cam is null)
        {
            return Task.FromResult(new PlcStatus(
                Connected: false,
                PlcReady: false,
                Ack: false,
                Busy: false,
                Done: false,
                Nok: false,
                Timeout: false,
                Conflict: false,
                Timestamp: now
            ));
        }

        try
        {
            _cam.Refresh();
            return Task.FromResult(new PlcStatus(
                Connected: true,
                PlcReady: _cam.PlcReady,
                Ack: _cam.Ack,
                Busy: _cam.Busy,
                Done: _cam.Done,
                Nok: _cam.Nok,
                Timeout: _cam.Timeout,
                Conflict: _cam.Conflict,
                Timestamp: now
            ));
        }
        catch
        {
            return Task.FromResult(new PlcStatus(false, false, false, false, false, false, false, false, now));
        }
    }

    public Task<bool> ExecuteStepAsync(StepAction action, int overallTimeoutMs, CancellationToken ct)
    {
        if (_plc is null || !_plc.IsConnected || _cam is null) return Task.FromResult(false);

        var dir = action switch
        {
            StepAction.LeftPlus => CamCorrHandshake.Direction.LeftPlus,
            StepAction.LeftMinus => CamCorrHandshake.Direction.LeftMinus,
            StepAction.RightPlus => CamCorrHandshake.Direction.RightPlus,
            StepAction.RightMinus => CamCorrHandshake.Direction.RightMinus,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported step action")
        };

        return Task.Run(() =>
        {
            try { return CamCorrHandshake.ExecuteOneStep(_cam, dir, overallTimeoutMs, pollMs: 10); }
            catch { return false; }
        }, ct);
    }

    public void Dispose()
    {
        try { if (_plc is { IsConnected: true }) _plc.Close(); } catch { }
        _plc = null;
        _cam = null;
    }
}
