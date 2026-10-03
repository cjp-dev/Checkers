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
/// An individual cached entry in the transposition table (compact 16-byte layout).
/// </summary>
public struct TranspositionEntry
{
    public ulong Key;
    public int Score;
    private sbyte _depth;
    private byte _boundAndAge;
    private byte _from;
    private byte _to;

    public int Depth
    {
        readonly get => _depth;
        set => _depth = (sbyte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue);
    }

    public TranspositionBound Bound
    {
        readonly get => (TranspositionBound)(_boundAndAge & 0x03);
        set => _boundAndAge = (byte)((_boundAndAge & 0xFC) | ((byte)value & 0x03));
    }

    public byte Age
    {
        readonly get => (byte)(_boundAndAge >> 2);
        set => _boundAndAge = (byte)((_boundAndAge & 0x03) | ((value & 0x3F) << 2));
    }

    public Position BestMoveFrom
    {
        readonly get => DecodePosition(_from);
        set => _from = EncodePosition(value);
    }

    public Position BestMoveTo
    {
        readonly get => DecodePosition(_to);
        set => _to = EncodePosition(value);
    }

    public readonly bool HasMove => (_from & 0x40) != 0 && (_to & 0x40) != 0;

    private static byte EncodePosition(Position pos)
    {
        if (!pos.IsValid) return 0xFF;
        if (pos.Row == 0 && pos.Col == 0) return 0;
        return (byte)(0x40 | ((pos.Row & 7) << 3) | (pos.Col & 7));
    }

    private static Position DecodePosition(byte code)
    {
        if (code == 0xFF) return new Position(-1, -1);
        if ((code & 0x40) == 0) return default;
        return new Position((code >> 3) & 7, code & 7);
    }
}

/// <summary>
/// High-performance transposition table with 64-bit Zobrist key hashing,
/// depth-preferred replacement, age management, and search telemetry.
/// </summary>
public sealed class TranspositionTable
{
    public const int DefaultEntries = 1_048_576; // 2^20 entries
    public const int MaxEntries = 16_777_216;    // 2^24 entries (matching Connect-4)

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
    public bool UsesFallbackCapacity { get; }

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
    /// <param name="megabytes">Approximate memory capacity in megabytes (default: 32 -> 1,048,576 entries).</param>
    public TranspositionTable(int megabytes = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(megabytes, 1);

        long desiredEntries = (long)megabytes * 1024 * 1024 / 32;
        int count = (int)BitOperations.RoundUpToPowerOf2((ulong)Math.Clamp(desiredEntries, 1024, 67_108_864));

        try
        {
            _entries = new TranspositionEntry[count];
            UsesFallbackCapacity = false;
        }
        catch (OutOfMemoryException) when (count > DefaultEntries)
        {
            count = DefaultEntries;
            _entries = new TranspositionEntry[count];
            UsesFallbackCapacity = true;
        }

        _mask = (ulong)(count - 1);
        _age = 0;
    }

    private TranspositionTable(int entries, bool isEntryCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(entries, 1024);

        int count = (int)BitOperations.RoundUpToPowerOf2((ulong)Math.Clamp(entries, 1024, 67_108_864));

        try
        {
            _entries = new TranspositionEntry[count];
            UsesFallbackCapacity = false;
        }
        catch (OutOfMemoryException) when (count > DefaultEntries)
        {
            count = DefaultEntries;
            _entries = new TranspositionEntry[count];
            UsesFallbackCapacity = true;
        }

        _mask = (ulong)(count - 1);
        _age = 0;
    }

    /// <summary>
    /// Creates a transposition table with the specified number of entries (rounded to the nearest power of two).
    /// </summary>
    public static TranspositionTable FromEntries(int entries) => new(entries, isEntryCount: true);

    /// <summary>
    /// Increments search age so entries from previous searches can be prioritized for replacement.
    /// </summary>
    public void NewSearch()
    {
        _age = (byte)((_age + 1) & 0x3F);
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
