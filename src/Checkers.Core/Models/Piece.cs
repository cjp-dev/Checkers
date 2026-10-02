namespace Checkers.Core.Models;

/// <summary>
/// Represents a checkers piece on the board.
/// </summary>
public readonly record struct Piece(PieceColor Color, PieceType Type)
{
    public bool IsKing => Type == PieceType.King;
    public bool IsMan => Type == PieceType.Man;

    public Piece Crown() => new(Color, PieceType.King);

    public override string ToString() => $"{(Color == PieceColor.White ? "W" : "B")}{(IsKing ? "K" : "M")}";
}
