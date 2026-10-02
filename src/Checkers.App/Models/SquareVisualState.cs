namespace Checkers.App.Models;

[Flags]
public enum SquareVisualState : byte
{
    None = 0,
    Selected = 1 << 0,
    ValidTarget = 1 << 1,
    LastMoveFrom = 1 << 2,
    LastMoveTo = 1 << 3,
    MandatoryCaptureSource = 1 << 4,
    Movable = 1 << 5,
    CapturedTarget = 1 << 6
}

