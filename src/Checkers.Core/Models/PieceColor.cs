namespace Checkers.Core.Models;

/// <summary>
/// Identifies the player piece color.
/// </summary>
public enum PieceColor : byte
{
    White = 0,
    Black = 1
}

public static class PieceColorExtensions
{
    public static PieceColor Opponent(this PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;
}
