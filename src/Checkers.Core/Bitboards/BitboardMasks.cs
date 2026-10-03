using System.Runtime.CompilerServices;
using Checkers.Core.Models;

namespace Checkers.Core.Bitboards;

/// <summary>
/// Precomputed 64-bit bitboard masks, diagonal rays, and coordinate lookup tables.
/// Square index sq = row * 8 + col (0..63), where row 0 is Black's home rank and row 7 is White's home rank.
/// </summary>
public static class BitboardMasks
{
    /// <summary>
    /// All 32 playable dark squares where (row + col) % 2 != 0.
    /// Row 0 (bits 0..7) has bits 1,3,5,7 set (0xAA); Row 1 (bits 8..15) has bits 8,10,12,14 set (0x55).
    /// </summary>
    public const ulong DarkSquares = 0x55AA55AA55AA55AAUL;

    /// <summary>
    /// Squares with column &gt;= 1 (excludes Column A / col 0).
    /// Used for 1-step left shifts (-9 up-left, +7 down-left).
    /// </summary>
    public const ulong NotColA = 0xFEFEFEFEFEFEFEFEUL;

    /// <summary>
    /// Squares with column &lt;= 6 (excludes Column H / col 7).
    /// Used for 1-step right shifts (-7 up-right, +9 down-right).
    /// </summary>
    public const ulong NotColH = 0x7F7F7F7F7F7F7F7FUL;

    /// <summary>
    /// Squares with column &gt;= 2 (excludes Columns A and B / cols 0..1).
    /// Used for 2-step left jumps (-18 up-left, +14 down-left).
    /// </summary>
    public const ulong NotColAB = 0xFCFCFCFCFCFCFCFCUL;

    /// <summary>
    /// Squares with column &lt;= 5 (excludes Columns G and H / cols 6..7).
    /// Used for 2-step right jumps (-14 up-right, +18 down-right).
    /// </summary>
    public const ulong NotColGH = 0x3F3F3F3F3F3F3F3FUL;

    /// <summary>
    /// Row 0 mask (White's promotion / crown rank, Black's back rank).
    /// </summary>
    public const ulong Row0 = 0x00000000000000FFUL;

    /// <summary>
    /// Row 7 mask (Black's promotion / crown rank, White's back rank).
    /// </summary>
    public const ulong Row7 = 0xFF00000000000000UL;

    /// <summary>
    /// Mask for each row 0..7.
    /// </summary>
    public static readonly ulong[] Rows = new ulong[8];

    /// <summary>
    /// Center 4 dark squares (rows 3..4, cols 2..5: Draughts squares #14, #15, #18, #19).
    /// </summary>
    public static readonly ulong CenterMask;

    /// <summary>
    /// Center 8 dark squares (rows 2..5, cols 2..5) used for English Checkers king centralization.
    /// </summary>
    public static readonly ulong KingCenterMask;

    /// <summary>
    /// Diagonal rays indexed by [dir, sq], where dir is:
    /// 0: (-1, -1) up-left   (delta -9)
    /// 1: (-1, +1) up-right  (delta -7)
    /// 2: (+1, -1) down-left (delta +7)
    /// 3: (+1, +1) down-right(delta +9)
    /// matches RuleEngine.AllDiagonals order.
    /// </summary>
    public static readonly ulong[,] Rays = new ulong[4, 64];

    /// <summary>
    /// Precomputed 1-step target square [dir, sq], or -1 if off-board.
    /// </summary>
    public static readonly sbyte[,] StepTarget = new sbyte[4, 64];

    /// <summary>
    /// Precomputed jumped square [dir, sq] for a 2-step jump, or -1 if the landing square is off-board.
    /// </summary>
    public static readonly sbyte[,] JumpMiddle = new sbyte[4, 64];

    /// <summary>
    /// Precomputed landing square [dir, sq] for a 2-step jump, or -1 if off-board.
    /// </summary>
    public static readonly sbyte[,] JumpLanding = new sbyte[4, 64];

    /// <summary>
    /// Precomputed Position(row, col) for each square index 0..63.
    /// </summary>
    public static readonly Position[] Positions = new Position[64];

    private static readonly (int dRow, int dCol)[] Diagonals =
    [
        (-1, -1), (-1, 1), (1, -1), (1, 1)
    ];

    static BitboardMasks()
    {
        for (int r = 0; r < 8; r++)
        {
            Rows[r] = 0xFFUL << (r * 8);
        }

        ulong center = 0UL;
        ulong kingCenter = 0UL;

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                int sq = r * 8 + c;
                Positions[sq] = new Position(r, c);

                if (((r + c) & 1) != 0)
                {
                    if (r is >= 3 and <= 4 && c is >= 2 and <= 5)
                    {
                        center |= 1UL << sq;
                    }
                    if (r is >= 2 and <= 5 && c is >= 2 and <= 5)
                    {
                        kingCenter |= 1UL << sq;
                    }
                }

                for (int d = 0; d < 4; d++)
                {
                    var (dr, dc) = Diagonals[d];
                    int r1 = r + dr;
                    int c1 = c + dc;
                    if (r1 is >= 0 and < 8 && c1 is >= 0 and < 8 && ((r1 + c1) & 1) != 0)
                    {
                        StepTarget[d, sq] = (sbyte)(r1 * 8 + c1);
                    }
                    else
                    {
                        StepTarget[d, sq] = -1;
                    }

                    int r2 = r + 2 * dr;
                    int c2 = c + 2 * dc;
                    if (r2 is >= 0 and < 8 && c2 is >= 0 and < 8 && ((r2 + c2) & 1) != 0)
                    {
                        JumpMiddle[d, sq] = (sbyte)(r1 * 8 + c1);
                        JumpLanding[d, sq] = (sbyte)(r2 * 8 + c2);
                    }
                    else
                    {
                        JumpMiddle[d, sq] = -1;
                        JumpLanding[d, sq] = -1;
                    }

                    ulong rayMask = 0UL;
                    int step = 1;
                    while (true)
                    {
                        int nr = r + step * dr;
                        int nc = c + step * dc;
                        if (nr is < 0 or >= 8 || nc is < 0 or >= 8 || ((nr + nc) & 1) == 0)
                            break;
                        rayMask |= 1UL << (nr * 8 + nc);
                        step++;
                    }
                    Rays[d, sq] = rayMask;
                }
            }
        }

        CenterMask = center;
        KingCenterMask = kingCenter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToSquareIndex(Position pos) => (pos.Row << 3) + pos.Col;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToSquareIndex(int row, int col) => (row << 3) + col;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Position ToPosition(int squareIndex) => Positions[squareIndex];
}
