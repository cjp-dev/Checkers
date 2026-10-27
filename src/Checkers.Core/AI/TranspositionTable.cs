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
/// <remarks>
/// The entry is two 64-bit words so that several search threads can share the table without locks
/// (the "lockless hashing" XOR trick). <c>_data</c> holds score, static eval, move, depth and flags;
/// <c>_keyWord</c> holds the 32-bit key XOR-ed with a fold of <c>_data</c>, plus the age.
/// A reader accepts the entry only if the key recomputed from both words matches, so an entry
/// that another thread is halfway through writing reads as a different key (a miss) instead of
/// returning a score from one position with the bound or depth of another.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct TranspositionEntry
{
    public const int NoStaticEval = 32_000;

    private const byte BoundMask = 0x03;
    private const byte OccupiedBit = 0x04;
    private const byte HasMoveBit = 0x08;
    private const byte HasStaticEvalBit = 0x10;

    private const int StaticEvalShift = 16;
    private const int MoveShift = 32;
    private const int DepthShift = 48;
    private const int FlagsShift = 56;
    private const int AgeShift = 32;

    // Bits 0-31: Key32 ^ Fold(_data); bits 32-39: age.
    private ulong _keyWord;

    // Bits 0-15 score, 16-31 static eval, 32-47 packed move, 48-55 depth, 56-63 flags.
    private ulong _data;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Fold(ulong data) => (uint)data ^ (uint)(data >> 32);

    /// <summary>
    /// Upper 32 bits of the 64-bit Zobrist hash ((uint)(key &gt;&gt; 32)).
    /// Combined with the bucket index bits, provides 50–54 bits of hash verification.
    /// </summary>
    public readonly uint Key32
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (uint)_keyWord ^ Fold(_data);
    }

    public readonly byte Age
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)(_keyWord >> AgeShift);
    }

    private readonly byte Flags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)(_data >> FlagsShift);
    }

    public readonly bool IsOccupied
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & OccupiedBit) != 0;
    }

    public readonly bool HasMove
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & HasMoveBit) != 0;
    }

    public readonly bool HasStaticEval
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & HasStaticEvalBit) != 0;
    }

    public readonly int Score
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (short)_data;
    }

    internal readonly int RawStaticEval
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (short)(_data >> StaticEvalShift);
    }

    public readonly int StaticEval
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => HasStaticEval ? RawStaticEval : NoStaticEval;
    }

    /// <summary>
    /// Packed 16-bit best move ((fromSq &lt;&lt; 8) | toSq).
    /// </summary>
    public readonly ushort BestMove
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ushort)(_data >> MoveShift);
    }

    public readonly int Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (sbyte)(_data >> DepthShift);
    }

    public readonly TranspositionBound Bound
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (TranspositionBound)(Flags & BoundMask);
    }

    public readonly Position BestMoveFrom => HasMove ? BitboardMasks.Positions[(BestMove >> 8) & 0x3F] : default;

    public readonly Position BestMoveTo => HasMove ? BitboardMasks.Positions[BestMove & 0x3F] : default;

    /// <summary>
    /// Takes a snapshot of the shared slot: key word first, then data (the writer stores them in the opposite order).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TranspositionEntry Load(ref TranspositionEntry slot)
    {
        TranspositionEntry copy;
        copy._keyWord = Volatile.Read(ref slot._keyWord);
        copy._data = Volatile.Read(ref slot._data);
        return copy;
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
        byte flags = (byte)(((byte)bound & BoundMask) | OccupiedBit);
        if (hasMove)
        {
            flags |= HasMoveBit;
        }

        ulong eval = 0;
        if (staticEval != NoStaticEval)
        {
            eval = (ushort)(short)Math.Clamp(staticEval, short.MinValue, short.MaxValue);
            flags |= HasStaticEvalBit;
        }

        ulong data =
            (ushort)(short)Math.Clamp(ttScore, short.MinValue, short.MaxValue)
            | (eval << StaticEvalShift)
            | ((ulong)bestPackedMove << MoveShift)
            | ((ulong)(byte)(sbyte)Math.Clamp(depth, sbyte.MinValue, sbyte.MaxValue) << DepthShift)
            | ((ulong)flags << FlagsShift);

        Volatile.Write(ref _data, data);
        Volatile.Write(ref _keyWord, (key32 ^ Fold(data)) | ((ulong)age << AgeShift));
    }
}

/// <summary>
/// High-performance 4-way set-associative transposition table (64-byte cache-line buckets)
/// with 32-bit upper Zobrist key verification, cached static evaluation, hardware cache prefetching,
/// age + depth + exact-bound victim replacement, and optional search telemetry.
/// Safe to share between search threads (see <see cref="TranspositionEntry"/>).
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

    // Telemetry counters, only maintained while CollectStatistics is on. They are plain (non-atomic) fields:
    // with several search threads they would race and make every thread write the same cache line.
    public bool CollectStatistics { get; set; }
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
    /// <param name="megabytes">Approximate memory capacity in megabytes (default: 16 -&gt; 1,048,576 entries of 16 bytes).</param>
    public TranspositionTable(int megabytes = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(megabytes, 1);

        long desiredEntries = (long)megabytes * 1024 * 1024 / Unsafe.SizeOf<TranspositionEntry>();
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
        bool stats = CollectStatistics;
        if (stats) Probes++;
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
            TranspositionEntry entry = TranspositionEntry.Load(ref Unsafe.Add(ref bucket0, i));
            if (!entry.IsOccupied)
            {
                return false;
            }

            if (entry.Key32 == key32)
            {
                if (stats) Hits++;
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
                        if (stats) Cutoffs++;
                    }
                }

                return true;
            }
        }

        if (stats) Collisions++;
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
        if (CollectStatistics) Stores++;
        int baseIndex = (int)(key & _bucketMask) << 2;
        uint key32 = (uint)(key >> 32);
        ref TranspositionEntry bucket0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_entries), baseIndex);

        int victimIndex = 0;
        int minPriority = int.MaxValue;

        for (int i = 0; i < BucketSize; i++)
        {
            ref TranspositionEntry slotRef = ref Unsafe.Add(ref bucket0, i);
            TranspositionEntry slot = TranspositionEntry.Load(ref slotRef);

            if (!slot.IsOccupied)
            {
                slotRef.Write(
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

                    slotRef.Write(
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
                    // Add the static eval to the deeper entry, keeping everything else (and its age).
                    slotRef.Write(
                        key32,
                        slot.Score,
                        slot.Depth,
                        slot.Bound,
                        slot.Age,
                        slot.BestMove,
                        slot.HasMove,
                        staticEval);
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

