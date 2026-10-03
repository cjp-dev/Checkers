using Checkers.Core.Models;

namespace Checkers.Core.Engine;

/// <summary>
/// Deterministic Zobrist hashing for Checkers board states.
/// </summary>
public static class Zobrist
{
    private static readonly ulong[,] PieceTable = new ulong[64, 4];
    private static readonly ulong BlackToMoveKey;

    static Zobrist()
    {
        // Deterministic PRNG with fixed seed
        var rng = new Random(1013904223);

        for (int sq = 0; sq < 64; sq++)
        {
            for (int p = 0; p < 4; p++)
            {
                PieceTable[sq, p] = NextUInt64(rng);
            }
        }

        BlackToMoveKey = NextUInt64(rng);
    }

    private static ulong NextUInt64(Random rng)
    {
        byte[] buffer = new byte[8];
        rng.NextBytes(buffer);
        return BitConverter.ToUInt64(buffer, 0);
    }

    public const int WhiteManIndex = 0;
    public const int WhiteKingIndex = 1;
    public const int BlackManIndex = 2;
    public const int BlackKingIndex = 3;

    public static int GetPieceIndex(Piece piece) =>
        (piece.Color == PieceColor.White ? 0 : 2) + (piece.IsKing ? 1 : 0);

    public static ulong GetPieceKey(int row, int col, Piece piece)
    {
        int sq = row * 8 + col;
        int p = GetPieceIndex(piece);
        return PieceTable[sq, p];
    }

    public static ulong GetPieceKey(int squareIndex, int pieceIndex) =>
        PieceTable[squareIndex, pieceIndex];

    public static ulong GetTurnKey(PieceColor activePlayer) =>
        activePlayer == PieceColor.Black ? BlackToMoveKey : 0UL;

    public static ulong TurnToggleKey => BlackToMoveKey;
}
