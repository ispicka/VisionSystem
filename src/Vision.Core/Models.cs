using System;

namespace Vision.Core;

public enum CameraId { Left = 0, Right = 1 }

public sealed record Frame(
    CameraId Camera,
    DateTimeOffset Timestamp,
    byte[] BgrBytes,
    int Width,
    int Height,
    int StrideBytes
);

public sealed record GapResult(
    DateTimeOffset Timestamp,
    double LeftGapMm,
    double RightGapMm,
    double Quality,
    string? Diagnostic = null
    );


public enum SideId { Left = 0, Right = 1 }

public sealed record SideGapResult(
    DateTimeOffset Timestamp,
    SideId Side,
    double GapMm,
    double Quality,
    string? Diagnostic = null
);

public sealed record StepCommand(
    DateTimeOffset Timestamp,
    SideId Side,
    StepAction Action,
    int Steps,
    string Reason
);



public enum ControlMode { Manual = 0, Auto = 1, AutoHold = 2 }

public enum StepAction
{
    None = 0,
    LeftPlus,
    LeftMinus,
    RightPlus,
    RightMinus
}

public sealed record ControlAction(
    DateTimeOffset Timestamp,
    StepAction Action,
    int Steps,
    string Reason
);

public sealed record PlcStatus(
    bool Connected,
    bool PlcReady,
    bool Ack,
    bool Busy,
    bool Done,
    bool Nok,
    bool Timeout,
    bool Conflict,
    DateTimeOffset Timestamp
);

public sealed record CameraStatus(
    bool Connected,
    double Fps,
    DateTimeOffset LastFrame,
    int DroppedFrames
);

public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    ControlMode Mode,
    PlcStatus Plc,
    CameraStatus CamLeft,
    CameraStatus CamRight,
    GapResult? LastGap,
    ControlAction? LastAction,
    string[] LastMessages
);
