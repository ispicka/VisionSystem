using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vision.Core;
using Vision.UI.Services;
using Vision.UI.Views;

namespace Vision.UI.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _cts = new();

    private object _currentView;
    public object CurrentView
    {
        get => _currentView;
        set { _currentView = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> LogLines { get; } = new();

    public string LeftGapText { get; private set; } = "L gap: n/a";
    public string RightGapText { get; private set; } = "R gap: n/a";
    public string LeftQualityText { get; private set; } = "Q: n/a";
    public string RightQualityText { get; private set; } = "Q: n/a";
    public string CamLeftText { get; private set; } = "CamL: n/a";
    public string CamRightText { get; private set; } = "CamR: n/a";
    public string PlcText { get; private set; } = "PLC: n/a";
    public string LastActionText { get; private set; } = "Last: n/a";

    public string PlcReadyText { get; private set; } = "Ready: -";
    public string PlcAckText { get; private set; } = "Ack: -";
    public string PlcBusyText { get; private set; } = "Busy: -";
    public string PlcDoneText { get; private set; } = "Done: -";
    public string PlcNokText { get; private set; } = "NOK: -";
    public string PlcTimeoutText { get; private set; } = "Timeout: -";
    public string PlcConflictText { get; private set; } = "Conflict: -";

    private int _modeIndex = 0;
    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            _modeIndex = value;
            OnPropertyChanged();
            ApplyModeFromIndex();
        }
    }

    // ===== Selected images =====
    private Bitmap? _leftBitmap;
    public Bitmap? LeftBitmap { get => _leftBitmap; private set { _leftBitmap = value; OnPropertyChanged(); } }

    private Bitmap? _rightBitmap;
    public Bitmap? RightBitmap { get => _rightBitmap; private set { _rightBitmap = value; OnPropertyChanged(); } }

    private string _leftImagePath = "n/a";
    public string LeftImagePath { get => _leftImagePath; private set { _leftImagePath = value; OnPropertyChanged(); } }

    private string _rightImagePath = "n/a";
    public string RightImagePath { get => _rightImagePath; private set { _rightImagePath = value; OnPropertyChanged(); } }

    // keep raw bytes so we can "recompute" without picking file again
    private byte[]? _leftRaw;
    private byte[]? _rightRaw;

    // Commands
    public ICommand ToggleAutoCommand { get; }
    public ICommand ResetHandshakeCommand { get; }

    public ICommand ShowDashboardCommand { get; }
    public ICommand ShowVisionCommand { get; }
    public ICommand ShowCamerasCommand { get; }
    public ICommand ShowRegPlcCommand { get; }

    public ICommand ManualLeftPlusCommand { get; }
    public ICommand ManualLeftMinusCommand { get; }
    public ICommand ManualRightPlusCommand { get; }
    public ICommand ManualRightMinusCommand { get; }

    public ICommand SelectLeftImageCommand { get; }
    public ICommand SelectRightImageCommand { get; }
    public ICommand ClearImagesCommand { get; }

    public ICommand RecomputeLeftCommand { get; }
    public ICommand RecomputeRightCommand { get; }

    public MainWindowViewModel()
    {
        _currentView = new DashboardView { DataContext = this };

        ToggleAutoCommand = new RelayCommand(_ => ToggleAuto());
        ResetHandshakeCommand = new RelayCommand(_ => EngineHost.State.RequestResetHandshake());

        ShowDashboardCommand = new RelayCommand(_ => CurrentView = new DashboardView { DataContext = this });
        ShowVisionCommand = new RelayCommand(_ => CurrentView = new VisionView { DataContext = this });
        ShowCamerasCommand = new RelayCommand(_ => CurrentView = new CamerasView { DataContext = this });
        ShowRegPlcCommand = new RelayCommand(_ => CurrentView = new RegPlcView { DataContext = this });

        ManualLeftPlusCommand = new RelayCommand(_ => ManualStep(SideId.Left, StepAction.LeftPlus));
        ManualLeftMinusCommand = new RelayCommand(_ => ManualStep(SideId.Left, StepAction.LeftMinus));
        ManualRightPlusCommand = new RelayCommand(_ => ManualStep(SideId.Right, StepAction.RightPlus));
        ManualRightMinusCommand = new RelayCommand(_ => ManualStep(SideId.Right, StepAction.RightMinus));

        SelectLeftImageCommand = new AsyncRelayCommand(async () => await PickImageAsync(isLeft: true));
        SelectRightImageCommand = new AsyncRelayCommand(async () => await PickImageAsync(isLeft: false));
        ClearImagesCommand = new RelayCommand(_ => ClearImages());

        RecomputeLeftCommand = new RelayCommand(_ => Recompute(isLeft: true));
        RecomputeRightCommand = new RelayCommand(_ => Recompute(isLeft: false));

        _ = Task.Run(async () =>
        {
            try { await EngineHost.StartAsync(ct: _cts.Token); }
            catch { }
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => RefreshSnapshot();
        _timer.Start();
    }

    private void ApplyModeFromIndex()
    {
        var st = EngineHost.State;
        if (st is null) return;
        st.Mode = _modeIndex switch
        {
            1 => ControlMode.Auto,
            2 => ControlMode.AutoHold,
            _ => ControlMode.Manual
        };
    }

    private void ToggleAuto()
    {
        var st = EngineHost.State;
        if (st is null) return;

        st.Mode = st.Mode == ControlMode.Auto ? ControlMode.Manual : ControlMode.Auto;

        _modeIndex = st.Mode switch
        {
            ControlMode.Auto => 1,
            ControlMode.AutoHold => 2,
            _ => 0
        };
        OnPropertyChanged(nameof(ModeIndex));
    }

    private void ManualStep(SideId side, StepAction action)
    {
        var st = EngineHost.State;
        if (st is null) return;
        st.RequestManualStep(new StepCommand(DateTimeOffset.UtcNow, side, action, 1, "UI manual"));
    }

    private void RefreshSnapshot()
    {
        var st = EngineHost.State;
        if (st is null) return;

        var snap = st.Snapshot();

        var idx = snap.Mode switch { ControlMode.Auto => 1, ControlMode.AutoHold => 2, _ => 0 };
        if (idx != _modeIndex)
        {
            _modeIndex = idx;
            OnPropertyChanged(nameof(ModeIndex));
        }

        if (snap.LastGap is not null)
        {
            LeftGapText = $"L gap: {snap.LastGap.LeftGapMm:F2} mm";
            RightGapText = $"R gap: {snap.LastGap.RightGapMm:F2} mm";
            LeftQualityText = $"Q: {snap.LastGap.Quality:F2}";
            RightQualityText = $"Q: {snap.LastGap.Quality:F2}";
        }
        else
        {
            LeftGapText = "L gap: n/a";
            RightGapText = "R gap: n/a";
            LeftQualityText = "Q: n/a";
            RightQualityText = "Q: n/a";
        }

        PlcText = snap.Plc.Connected ? "PLC: Connected" : "PLC: Disconnected";
        PlcReadyText = $"Ready: {(snap.Plc.PlcReady ? "1" : "0")}";
        PlcAckText = $"Ack: {(snap.Plc.Ack ? "1" : "0")}";
        PlcBusyText = $"Busy: {(snap.Plc.Busy ? "1" : "0")}";
        PlcDoneText = $"Done: {(snap.Plc.Done ? "1" : "0")}";
        PlcNokText = $"NOK: {(snap.Plc.Nok ? "1" : "0")}";
        PlcTimeoutText = $"Timeout: {(snap.Plc.Timeout ? "1" : "0")}";
        PlcConflictText = $"Conflict: {(snap.Plc.Conflict ? "1" : "0")}";

        LastActionText = snap.LastAction is null ? "Last: n/a" : $"Last: {snap.LastAction.Action} ({snap.LastAction.Reason})";

        CamLeftText = snap.CamLeft.Connected ? "CamL: OK" : "CamL: n/a";
        CamRightText = snap.CamRight.Connected ? "CamR: OK" : "CamR: n/a";

        LogLines.Clear();
        foreach (var line in snap.LastMessages) LogLines.Add(line);

        OnPropertyChanged(nameof(LeftGapText));
        OnPropertyChanged(nameof(RightGapText));
        OnPropertyChanged(nameof(LeftQualityText));
        OnPropertyChanged(nameof(RightQualityText));
        OnPropertyChanged(nameof(CamLeftText));
        OnPropertyChanged(nameof(CamRightText));
        OnPropertyChanged(nameof(PlcText));
        OnPropertyChanged(nameof(PlcReadyText));
        OnPropertyChanged(nameof(PlcAckText));
        OnPropertyChanged(nameof(PlcBusyText));
        OnPropertyChanged(nameof(PlcDoneText));
        OnPropertyChanged(nameof(PlcNokText));
        OnPropertyChanged(nameof(PlcTimeoutText));
        OnPropertyChanged(nameof(PlcConflictText));
        OnPropertyChanged(nameof(LastActionText));
    }

    private async Task PickImageAsync(bool isLeft)
    {
        try
        {
            var top = TryGetTopLevel();
            if (top?.StorageProvider is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = isLeft ? "Vyber Left obrázek" : "Vyber Right obrázek",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff"]
                    }
                ]
            });

            if (files is null || files.Count == 0) return;

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            // load UI bitmap
            await using (var fs = File.OpenRead(path))
            {
                var bmp = new Bitmap(fs);

                if (isLeft)
                {
                    LeftBitmap?.Dispose();
                    LeftBitmap = bmp;
                    LeftImagePath = path;
                }
                else
                {
                    RightBitmap?.Dispose();
                    RightBitmap = bmp;
                    RightImagePath = path;
                }
            }

            // store raw for recompute
            var raw = File.ReadAllBytes(path);
            if (isLeft) _leftRaw = raw;
            else _rightRaw = raw;

            // push test frame to engine (immediate compute)
            PushTestFrame(isLeft);
        }
        catch (Exception ex)
        {
            EngineHost.State?.AddMsg("File pick error: " + ex.Message);
        }
    }

    private void Recompute(bool isLeft)
    {
        // re-send frozen frame with new timestamp -> triggers compute again
        PushTestFrame(isLeft);
    }

    private void PushTestFrame(bool isLeft)
    {
        var st = EngineHost.State;
        if (st is null) return;

        var raw = isLeft ? _leftRaw : _rightRaw;
        if (raw is null || raw.Length == 0)
        {
            st.AddMsg(isLeft ? "No LEFT image for recompute." : "No RIGHT image for recompute.");
            return;
        }

        var frame = new Frame(
            Camera: isLeft ? CameraId.Left : CameraId.Right,
            Timestamp: DateTimeOffset.UtcNow,
            BgrBytes: raw, // placeholder: raw file bytes (algorithm later will decode)
            Width: 0,
            Height: 0,
            StrideBytes: 0
        );

        if (isLeft) st.SetTestFrameLeft(frame);
        else st.SetTestFrameRight(frame);
    }

    private void ClearImages()
    {
        LeftBitmap?.Dispose();
        RightBitmap?.Dispose();
        LeftBitmap = null;
        RightBitmap = null;
        LeftImagePath = "n/a";
        RightImagePath = "n/a";
        _leftRaw = null;
        _rightRaw = null;
    }

    private static TopLevel? TryGetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Commands
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> run, Func<object?, bool>? canExecute = null)
    {
        _run = run;
        _can = canExecute;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _run(parameter);

    public event EventHandler? CanExecuteChanged;
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _run;
    private bool _busy;

    public AsyncRelayCommand(Func<Task> run) => _run = run;

    public bool CanExecute(object? parameter) => !_busy;

    public async void Execute(object? parameter)
    {
        if (_busy) return;
        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _run(); }
        finally
        {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CanExecuteChanged;
}
