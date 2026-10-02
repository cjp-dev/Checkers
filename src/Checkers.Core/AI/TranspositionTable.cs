using System.Numerics;
using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Specifies the nature of an evaluation score in the transposition table.
/// </summary>
public enum TranspositionBound : byte
{
    /// <summary>Exact minimax evaluation score.</summary>
    Exact = 0,

    /// <summary>Lower bound score (caused a beta cutoff, true score &gt;= score).</summary>
    LowerBound = 1,

    /// <summary>Upper bound score (alpha fail-low, true score &lt;= score).</summary>
    UpperBound = 2
}

/// <summary>
/// An individual cached entry in the transposition table.
/// </summary>
public struct TranspositionEntry
{
    public ulong Key;
    public int Score;
    public int Depth;
    public TranspositionBound Bound;
    public byte Age;
    public Position BestMoveFrom;
    public Position BestMoveTo;

    public readonly bool HasMove => BestMoveFrom.IsValid && BestMoveTo.IsValid;
}

/// <summary>
/// High-performance transposition table with 64-bit Zobrist key hashing,
/// depth-preferred replacement, age management, and search telemetry.
/// </summary>
public sealed class TranspositionTable
{
    private const int WinThreshold = 90_000;

    private readonly TranspositionEntry[] _entries;
    private readonly ulong _mask;
    private byte _age;

    // Telemetry counters
    public long Probes { get; private set; }
    public long Hits { get; private set; }
    public long Cutoffs { get; private set; }
    public long Collisions { get; private set; }
    public long Stores { get; private set; }

    public int Capacity => _entries.Length;

    public int OccupiedEntries
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Key != 0) count++;
            }
            return count;
        }
    }

    public double FillPercentage => (double)OccupiedEntries / Capacity * 100.0;

    /// <summary>
    /// Initializes a transposition table sized to the nearest power-of-two number of entries.
    /// </summary>
    /// <param name="megabytes">Approximate memory capacity in megabytes (default: 32 MB).</param>
    public TranspositionTable(int megabytes = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(megabytes, 1);

        // Approximate 32 bytes per entry
        long desiredEntries = (long)megabytes * 1024 * 1024 / 32;
        int count = (int)BitOperations.RoundUpToPowerOf2((ulong)Math.Clamp(desiredEntries, 1024, 67_108_864));

        _entries = new TranspositionEntry[count];
        _mask = (ulong)(count - 1);
        _age = 0;
    }

    /// <summary>
    /// Increments search age so entries from previous searches can be prioritized for replacement.
    /// </summary>
    public void NewSearch()
    {
        _age++;
    }

    /// <summary>
    /// Clears all table entries and resets telemetry.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_entries, 0, _entries.Length);
        ResetStats();
        _age = 0;
    }

    public void ResetStats()
    {
        Probes = 0;
        Hits = 0;
        Cutoffs = 0;
        Collisions = 0;
        Stores = 0;
    }

    /// <summary>
    /// Probes the table for a cached evaluation and/or PV move.
    /// </summary>
    public bool TryProbe(
        ulong key,
        int depth,
        int alpha,
        int beta,
        int ply,
        out int score,
        out Position bestFrom,
        out Position bestTo,
        out bool hasCutoff)
    {
        Probes++;
        ulong index = key & _mask;
        ref readonly var entry = ref _entries[index];

        score = 0;
        bestFrom = default;
        bestTo = default;
        hasCutoff = false;

        if (entry.Key == 0)
        {
            return false;
        }

        if (entry.Key != key)
        {
            Collisions++;
            return false;
        }

        Hits++;
        bestFrom = entry.BestMoveFrom;
        bestTo = entry.BestMoveTo;

        if (entry.Depth >= depth)
        {
            int adjustedScore = ScoreFromTt(entry.Score, ply);

            if (entry.Bound == TranspositionBound.Exact)
            {
                score = adjustedScore;
                hasCutoff = true;
                Cutoffs++;
                return true;
            }

            if (entry.Bound == TranspositionBound.LowerBound && adjustedScore >= beta)
            {
                score = adjustedScore;
                hasCutoff = true;
                Cutoffs++;
                return true;
            }

            if (entry.Bound == TranspositionBound.UpperBound && adjustedScore <= alpha)
            {
                score = adjustedScore;
                hasCutoff = true;
                Cutoffs++;
                return true;
            }
        }

        return true;
    }

    /// <summary>
    /// Stores an evaluated position with bound and optional best move.
    /// </summary>
    public void Store(
        ulong key,
        int depth,
        int score,
        TranspositionBound bound,
        int ply,
        Position bestFrom = default,
        Position bestTo = default)
    {
        Stores++;
        ulong index = key & _mask;
        ref var entry = ref _entries[index];

        bool isSlotEmpty = entry.Key == 0;
        bool isSameKey = entry.Key == key;
        bool isOlderSearch = entry.Age != _age;
        bool isDeeperOrEqual = depth >= entry.Depth;

        // Replace if empty, same position, older generation, or deeper depth
        if (isSlotEmpty || isSameKey || isOlderSearch || isDeeperOrEqual)
        {
            entry.Key = key;
            entry.Score = ScoreToTt(score, ply);
            entry.Depth = depth;
            entry.Bound = bound;
            entry.Age = _age;

            if (bestFrom.IsValid && bestTo.IsValid)
            {
                entry.BestMoveFrom = bestFrom;
                entry.BestMoveTo = bestTo;
            }
            else if (!isSameKey)
            {
                entry.BestMoveFrom = default;
                entry.BestMoveTo = default;
            }
        }
    }

    public static int ScoreToTt(int score, int ply)
    {
        if (score >= WinThreshold) return score + ply;
        if (score <= -WinThreshold) return score - ply;
        return score;
    }

    public static int ScoreFromTt(int score, int ply)
    {
        if (score >= WinThreshold) return score - ply;
        if (score <= -WinThreshold) return score + ply;
        return score;
    }
}
