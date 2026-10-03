namespace Checkers.Core.Models;

/// <summary>
/// Selects the underlying board representation and move/search implementation.
/// </summary>
public enum BoardEngine
{
    /// <summary>
    /// 64-bit bitboard representation (4 x ulong) with bitwise move generation, popcount evaluation,
    /// incremental Zobrist hashing, and zero-allocation copy-make search.
    /// </summary>
    Bitboard = 0,

    /// <summary>
    /// Classic 8x8 2D array (Piece?[,]) representation and object-based search.
    /// </summary>
    Array = 1
}
