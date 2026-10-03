using System.Numerics;
using System.Runtime.CompilerServices;
using Checkers.Core.Models;

namespace Checkers.Core.Bitboards;

/// <summary>
/// Compact, allocation-free representation of a legal move for bitboard search.
/// Fits in 16 bytes (ulong Captured + byte From + byte To + bool IsPromotion).
/// </summary>
public readonly struct BitMove
{
    /// <summary>
    /// 64-bit bitmask of all opponent squares captured during this move (0 for quiet moves).
    /// </summary>
    public readonly ulong Captured;

    /// <summary>
    /// Origin square index (0..63).
    /// </summary>
    public readonly byte From;

    /// <summary>
    /// Destination square index (0..63).
    /// </summary>
    public readonly byte To;

    /// <summary>
    /// True if a regular Man reaches the crown rank and promotes to a King at the end of this move.
    /// </summary>
    public readonly bool IsPromotion;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitMove(byte from, byte to, ulong captured = 0UL, bool isPromotion = false)
    {
        From = from;
        To = to;
        Captured = captured;
        IsPromotion = isPromotion;
    }

    public bool IsCapture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Captured != 0UL;
    }

    public int CaptureCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitOperations.PopCount(Captured);
    }

    public static BitMove FromMove(Move move)
    {
        ulong capturedMask = 0UL;
        var caps = move.CapturedPositions;
        for (int i = 0; i < caps.Count; i++)
        {
            capturedMask |= 1UL << BitboardMasks.ToSquareIndex(caps[i]);
        }

        return new BitMove(
            from: (byte)BitboardMasks.ToSquareIndex(move.From),
            to: (byte)BitboardMasks.ToSquareIndex(move.To),
            captured: capturedMask,
            isPromotion: move.IsPromotion);
    }
}
