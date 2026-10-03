using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Checkers.Core.Bitboards;
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
/// An individual cached entry in the transposition table (compact 16-byte layout, 4 entries = 64-byte CPU cache line).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TranspositionEntry
{
    public const int NoStaticEval = 32_000;

    private const byte BoundMask = 0x03;
    private const byte OccupiedBit = 0x04;
    private const byte HasMoveBit = 0x08;
    private const byte HasStaticEvalBit = 0x10;

    /// <summary>
    /// Upper 32 bits of the 64-bit Zobrist hash ((uint)(key &gt;&gt; 32)).
    /// Combined with the bucket index bits, provides 50–54 bits of hash verification.
    /// </summary>
    public uint Key32;

    private short _score;
    private short _staticEval;

    /// <summary>
    /// Packed 16-bit best move ((fromSq &lt;&lt; 8) | toSq).
    /// </summary>
    public ushort BestMove;

    private sbyte _depth;
    public byte Age;
    private byte _flags;
    private byte _pad1;
    private byte _pad2;
    private byte _pad3;

    public readonly bool IsOccupied
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_flags & OccupiedBit) != 0;
    }

    public readonly bool HasMove
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_flags & HasMoveBit) != 0;
    }

    public readonly bool HasStaticEval
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_flags & HasStaticEvalBit) != 0;
    }

    public int Score
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => _score;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _score = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
    }

    internal readonly int RawStaticEval
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _staticEval;
    }

    public int StaticEval
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => HasStaticEval ? _staticEval : NoStaticEval;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (value == NoStaticEval)
            {
                _staticEval = 0;
                _flags = (byte)(_flags & ~HasStaticEvalBit);
            }
            else
            {
                _staticEval = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
                _flags |= HasStaticEvalBit;
            }
        }
    }

    public int Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => _depth;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _depth = (sbyte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue);
    }

    public TranspositionBound Bound
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => (TranspositionBound)(_flags & BoundMask);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _flags = (byte)((_flags & ~BoundMask) | ((byte)value & BoundMask));
    }

    public Position BestMoveFrom
    {
        readonly get => HasMove ? BitboardMasks.Positions[(BestMove >> 8) & 0x3F] : default;
        set
        {
            if (!value.IsValid)
            {
                _flags = (byte)(_flags & ~HasMoveBit);
                return;
            }
            int fromSq = BitboardMasks.ToSquareIndex(value);
            BestMove = (ushort)((fromSq << 8) | (BestMove & 0xFF));
            _flags |= HasMoveBit;
        }
    }

    public Position BestMoveTo
    {
        readonly get => HasMove ? BitboardMasks.Positions[BestMove & 0x3F] : default;
        set
        {
            if (!value.IsValid)
            {
                _flags = (byte)(_flags & ~HasMoveBit);
                return;
            }
            int toSq = BitboardMasks.ToSquareIndex(value);
            BestMove = (ushort)((BestMove & 0xFF00) | toSq);
            _flags |= HasMoveBit;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Write(
        uint key32,
        int ttScore,
        int depth,
        TranspositionBound bound,
        byte age,
        ushort bestPackedMove,
        bool hasMove,
        int staticEval)
    {
        Key32 = key32;
        _score = (short)Math.Clamp(ttScore, short.MinValue, short.MaxValue);
        _depth = (sbyte)Math.Clamp(depth, sbyte.MinValue, sbyte.MaxValue);
        Age = age;
        BestMove = bestPackedMove;

        byte flags = (byte)(((byte)bound & BoundMask) | OccupiedBit);
        if (hasMove)
        {
            flags |= HasMoveBit;
        }
        if (staticEval != NoStaticEval)
        {
            _staticEval = (short)Math.Clamp(staticEval, short.MinValue, short.MaxValue);
            flags |= HasStaticEvalBit;
        }
        else
        {
            _staticEval = 0;
        }
        _flags = flags;
    }
}

/// <summary>
/// High-performance 4-way set-associative transposition table (64-byte cache-line buckets)
/// with 32-bit upper Zobrist key verification, cached static evaluation, hardware cache prefetching,
/// age + depth + exact-bound victim replacement, and search telemetry.
/// </summary>
public sealed class TranspositionTable
{
    public const int DefaultEntries = 1_048_576; // 2^20 entries (262,144 4-way buckets = 16 MiB)
    public const int MaxEntries = 16_777_216;    // 2^24 entries (4,194,304 4-way buckets = 256 MiB)
    public const int BucketSize = 4;             // 4 entries * 16B = 64B (1 CPU cache line)

    internal const int WinThreshold = 28_000;

    private readonly TranspositionEntry[] _entries;
    private readonly ulong _bucketMask;
    private byte _age;

    // Telemetry counters
    public long Probes { get; private set; }
    public long Hits { get; private set; }
    public long Cutoffs { get; private set; }
    public long Collisions { get; private set; }
    public long Stores { get; private set; }

    public int Capacity => _entries.Length;
    public int BucketCount => _entries.Length >> 2;
    public bool UsesFallbackCapacity { get; }

    public int OccupiedEntries
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].IsOccupied) count++;
            }
            return count;
        }
    }

    public double FillPercentage => (double)OccupiedEntries / Capacity * 100.0;

    /// <summary>
    /// Initializes a transposition table sized to the nearest power-of-two number of entries.
    /// </summary>
    /// <param name="megabytes">Approximate memory capacity in megabytes (default: 32 -&gt; 1,048,576 entries).</param>
    public TranspositionTable(int megabytes = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(megabytes, 1);

        long desiredEntries = (long)megabytes * 1024 * 1024 / 32;
        int count = (int)BitOperations.RoundUpToPowerOf2((ulong)Math.Clamp(desiredEntries, 1024, 67_108_864));

        try
        {
            _entries = GC.AllocateArray<TranspositionEntry>(count, pinned: true);
            UsesFallbackCapacity = false;
        }
        catch (OutOfMemoryException) when (count > DefaultEntries)
        {
            count = DefaultEntries;
            _entries = GC.AllocateArray<TranspositionEntry>(count, pinned: true);
            UsesFallbackCapacity = true;
        }

        _bucketMask = (ulong)((count >> 2) - 1);
        _age = 0;
    }

    private TranspositionTable(int entries, bool isEntryCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(entries, 1024);

        int count = (int)BitOperations.RoundUpToPowerOf2((ulong)Math.Clamp(entries, 1024, 67_108_864));

        try
        {
            _entries = GC.AllocateArray<TranspositionEntry>(count, pinned: true);
            UsesFallbackCapacity = false;
        }
        catch (OutOfMemoryException) when (count > DefaultEntries)
        {
            count = DefaultEntries;
            _entries = GC.AllocateArray<TranspositionEntry>(count, pinned: true);
            UsesFallbackCapacity = true;
        }

        _bucketMask = (ulong)((count >> 2) - 1);
        _age = 0;
    }

    /// <summary>
    /// Creates a transposition table with the specified number of entries (rounded to the nearest power of two).
    /// </summary>
    public static TranspositionTable FromEntries(int entries) => new(entries, isEntryCount: true);

    /// <summary>
    /// Issues a non-blocking hardware L1 cache prefetch for the 64-byte bucket corresponding to <paramref name="key"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Prefetch(ulong key)
    {
        if (Sse.IsSupported)
        {
            ref TranspositionEntry bucket0 = ref Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(_entries),
                (int)(key & _bucketMask) << 2);
            Sse.Prefetch0(Unsafe.AsPointer(ref bucket0));
        }
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
    /// Fast bitboard-native probe returning packed 16-bit best move and cached static evaluation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryProbe(
        ulong key,
        int depth,
        int alpha,
        int beta,
        int ply,
        out int score,
        out ushort bestPackedMove,
        out bool hasBestMove,
        out int staticEval,
        out bool hasStaticEval,
        out bool hasCutoff)
    {
        Probes++;
        int baseIndex = (int)(key & _bucketMask) << 2;
        uint key32 = (uint)(key >> 32);
        ref TranspositionEntry bucket0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_entries), baseIndex);

        score = 0;
        bestPackedMove = 0;
        hasBestMove = false;
        staticEval = TranspositionEntry.NoStaticEval;
        hasStaticEval = false;
        hasCutoff = false;

        for (int i = 0; i < BucketSize; i++)
        {
            ref readonly TranspositionEntry entry = ref Unsafe.Add(ref bucket0, i);
            if (!entry.IsOccupied)
            {
                return false;
            }

            if (entry.Key32 == key32)
            {
                Hits++;
                hasBestMove = entry.HasMove;
                bestPackedMove = entry.BestMove;
                hasStaticEval = entry.HasStaticEval;
                staticEval = entry.RawStaticEval;

                if (entry.Depth >= depth)
                {
                    int adjustedScore = ScoreFromTt(entry.Score, ply);
                    TranspositionBound bound = entry.Bound;

                    if (bound == TranspositionBound.Exact ||
                        (bound == TranspositionBound.LowerBound && adjustedScore >= beta) ||
                        (bound == TranspositionBound.UpperBound && adjustedScore <= alpha))
                    {
                        score = adjustedScore;
                        hasCutoff = true;
                        Cutoffs++;
                    }
                }

                return true;
            }
        }

        Collisions++;
        return false;
    }

    /// <summary>
    /// Probes the table for a cached evaluation and/or PV move using <see cref="Position"/> coordinates.
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
        bool found = TryProbe(
            key,
            depth,
            alpha,
            beta,
            ply,
            out score,
            out ushort bestPackedMove,
            out bool hasBestMove,
            out _,
            out _,
            out hasCutoff);

        if (found && hasBestMove)
        {
            bestFrom = BitboardMasks.Positions[(bestPackedMove >> 8) & 0x3F];
            bestTo = BitboardMasks.Positions[bestPackedMove & 0x3F];
        }
        else
        {
            bestFrom = default;
            bestTo = default;
        }

        return found;
    }

    /// <summary>
    /// Fast bitboard-native store into the 4-way cache-line bucket using Age + Depth + Exact-Bound victim selection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Store(
        ulong key,
        int depth,
        int score,
        TranspositionBound bound,
        int ply,
        ushort bestPackedMove,
        bool hasBestMove,
        int staticEval = TranspositionEntry.NoStaticEval)
    {
        Stores++;
        int baseIndex = (int)(key & _bucketMask) << 2;
        uint key32 = (uint)(key >> 32);
        ref TranspositionEntry bucket0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_entries), baseIndex);

        int victimIndex = 0;
        int minPriority = int.MaxValue;

        for (int i = 0; i < BucketSize; i++)
        {
            ref TranspositionEntry slot = ref Unsafe.Add(ref bucket0, i);

            if (!slot.IsOccupied)
            {
                slot.Write(
                    key32,
                    ScoreToTt(score, ply),
                    depth,
                    bound,
                    _age,
                    bestPackedMove,
                    hasBestMove,
                    staticEval);
                return;
            }

            if (slot.Key32 == key32)
            {
                if (slot.Age != _age || depth >= slot.Depth || bound == TranspositionBound.Exact)
                {
                    ushort moveToStore = hasBestMove ? bestPackedMove : slot.BestMove;
                    bool moveFlag = hasBestMove || slot.HasMove;
                    int evalToStore = staticEval != TranspositionEntry.NoStaticEval
                        ? staticEval
                        : (slot.HasStaticEval ? slot.RawStaticEval : TranspositionEntry.NoStaticEval);

                    slot.Write(
                        key32,
                        ScoreToTt(score, ply),
                        depth,
                        bound,
                        _age,
                        moveToStore,
                        moveFlag,
                        evalToStore);
                }
                else if (staticEval != TranspositionEntry.NoStaticEval && !slot.HasStaticEval)
                {
                    slot.StaticEval = staticEval;
                }
                return;
            }

            int staleness = (_age - slot.Age) & 0xFF;
            int priority = slot.Depth - (staleness << 3) + (slot.Bound == TranspositionBound.Exact ? 4 : 0);
            if (priority < minPriority)
            {
                minPriority = priority;
                victimIndex = i;
            }
        }

        ref TranspositionEntry victim = ref Unsafe.Add(ref bucket0, victimIndex);
        victim.Write(
            key32,
            ScoreToTt(score, ply),
            depth,
            bound,
            _age,
            bestPackedMove,
            hasBestMove,
            staticEval);
    }

    /// <summary>
    /// Stores an evaluated position with bound and optional <see cref="Position"/> best move.
    /// </summary>
    public void Store(
        ulong key,
        int depth,
        int score,
        TranspositionBound bound,
        int ply,
        Position bestFrom = default,
        Position bestTo = default,
        int staticEval = TranspositionEntry.NoStaticEval)
    {
        bool hasBestMove = bestFrom.IsValid && bestTo.IsValid && (bestFrom.Row != 0 || bestFrom.Col != 0 || bestTo.Row != 0 || bestTo.Col != 0);
        ushort bestPackedMove = hasBestMove
            ? (ushort)((BitboardMasks.ToSquareIndex(bestFrom) << 8) | BitboardMasks.ToSquareIndex(bestTo))
            : (ushort)0;

        Store(key, depth, score, bound, ply, bestPackedMove, hasBestMove, staticEval);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ScoreToTt(int score, int ply)
    {
        if (score >= WinThreshold) return score + ply;
        if (score <= -WinThreshold) return score - ply;
        return score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ScoreFromTt(int score, int ply)
    {
        if (score >= WinThreshold) return score - ply;
        if (score <= -WinThreshold) return score + ply;
        return score;
    }
}

