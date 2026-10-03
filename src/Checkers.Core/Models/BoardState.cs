using Checkers.Core.Bitboards;

namespace Checkers.Core.Models;

/// <summary>
/// Snapshot representing the complete state of the 8x8 checkers board,
/// backed directly by a 64-bit <see cref="Bitboards.BitPosition"/> (4 × ulong bitboards).
/// </summary>
public sealed class BoardState
{
    private BitPosition _bitPosition;

    /// <summary>
    /// Gets the underlying value-type 64-bit bitboard position.
    /// </summary>
    public BitPosition BitPosition => _bitPosition;

    public PieceColor ActivePlayer
    {
        get => _bitPosition.SideToMove;
        set => _bitPosition.SideToMove = value;
    }

    public int HalfMoveClock
    {
        get => _bitPosition.HalfMoveClock;
        set => _bitPosition.HalfMoveClock = value;
    }

    public int FullMoveNumber { get; set; } = 1;

    public ulong ZobristHash
    {
        get => _bitPosition.Hash;
        set => _bitPosition.Hash = value;
    }

    public int WhitePiecesCount => _bitPosition.WhitePiecesCount;
    public int BlackPiecesCount => _bitPosition.BlackPiecesCount;
    public int WhiteKingsCount => _bitPosition.WhiteKingsCount;
    public int BlackKingsCount => _bitPosition.BlackKingsCount;

    public BoardState()
    {
        _bitPosition = new BitPosition
        {
            SideToMove = PieceColor.White
        };
    }

    public BoardState(BitPosition bitPosition, int fullMoveNumber = 1)
    {
        _bitPosition = bitPosition;
        FullMoveNumber = fullMoveNumber;
    }

    public Piece? GetPiece(Position pos) =>
        _bitPosition.GetPiece(pos.Row, pos.Col);

    public Piece? GetPiece(int row, int col) =>
        _bitPosition.GetPiece(row, col);

    public void SetPiece(Position pos, Piece? piece)
    {
        if (!pos.IsValid)
            throw new ArgumentOutOfRangeException(nameof(pos), "Position is outside board bounds.");

        _bitPosition.SetPiece(pos.Row, pos.Col, piece);
    }

    public ulong RecalculateHash() =>
        _bitPosition.RecalculateHash();

    public BoardState Clone() =>
        new(_bitPosition, FullMoveNumber);

    public static BoardState CreateEmpty(PieceColor activePlayer = PieceColor.White) =>
        new(BitPosition.CreateEmpty(activePlayer), fullMoveNumber: 1);

    public static BoardState CreateInitial() =>
        new(BitPosition.CreateInitial(), fullMoveNumber: 1);
}
