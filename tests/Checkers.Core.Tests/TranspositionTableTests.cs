using Checkers.Core.AI;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class TranspositionTableTests
{
    [Fact]
    public void Constructor_InitializesWithPowerOfTwoCapacity()
    {
        var tt = new TranspositionTable(megabytes: 16);
        tt.Capacity.Should().BeGreaterThan(0);
        (tt.Capacity & (tt.Capacity - 1)).Should().Be(0, "Capacity should be a power of two");
    }

    [Fact]
    public void StoreAndProbe_ExactScore_ReturnsHitAndCutoff()
    {
        var tt = new TranspositionTable(megabytes: 1);
        ulong key = 0x123456789ABCDEF0;
        var from = new Position(5, 0);
        var to = new Position(4, 1);

        tt.Store(key, depth: 4, score: 150, bound: TranspositionBound.Exact, ply: 0, from, to);

        bool found = tt.TryProbe(key, depth: 4, alpha: -1000, beta: 1000, ply: 0,
            out int score, out var bestFrom, out var bestTo, out bool hasCutoff);

        found.Should().BeTrue();
        hasCutoff.Should().BeTrue();
        score.Should().Be(150);
        bestFrom.Should().Be(from);
        bestTo.Should().Be(to);
        tt.Hits.Should().Be(1);
        tt.Cutoffs.Should().Be(1);
    }

    [Fact]
    public void Probe_LowerBound_CausesCutoffOnlyWhenAtLeastBeta()
    {
        var tt = new TranspositionTable(megabytes: 1);
        ulong key = 0xAAAAAAAAAAAAAAAA;

        tt.Store(key, depth: 4, score: 200, bound: TranspositionBound.LowerBound, ply: 0);

        // Beta is 150: score (200) >= beta (150) -> cutoff
        tt.TryProbe(key, depth: 4, alpha: 0, beta: 150, ply: 0,
            out int score1, out _, out _, out bool hasCutoff1);
        hasCutoff1.Should().BeTrue();
        score1.Should().Be(200);

        // Beta is 300: score (200) < beta (300) -> NO cutoff
        tt.TryProbe(key, depth: 4, alpha: 0, beta: 300, ply: 0,
            out _, out _, out _, out bool hasCutoff2);
        hasCutoff2.Should().BeFalse();
    }

    [Fact]
    public void Probe_UpperBound_CausesCutoffOnlyWhenAtMostAlpha()
    {
        var tt = new TranspositionTable(megabytes: 1);
        ulong key = 0xBBBBBBBBBBBBBBBB;

        tt.Store(key, depth: 4, score: -100, bound: TranspositionBound.UpperBound, ply: 0);

        // Alpha is -50: score (-100) <= alpha (-50) -> cutoff
        tt.TryProbe(key, depth: 4, alpha: -50, beta: 50, ply: 0,
            out int score1, out _, out _, out bool hasCutoff1);
        hasCutoff1.Should().BeTrue();
        score1.Should().Be(-100);

        // Alpha is -200: score (-100) > alpha (-200) -> NO cutoff
        tt.TryProbe(key, depth: 4, alpha: -200, beta: 50, ply: 0,
            out _, out _, out _, out bool hasCutoff2);
        hasCutoff2.Should().BeFalse();
    }

    [Fact]
    public void Probe_ShallowDepth_ProvidesBestMoveWithoutCutoff()
    {
        var tt = new TranspositionTable(megabytes: 1);
        ulong key = 0xCCCCCCCCCCCCCCCC;
        var from = new Position(5, 2);
        var to = new Position(4, 3);

        tt.Store(key, depth: 2, score: 50, bound: TranspositionBound.Exact, ply: 0, from, to);

        // Requested depth is 4 (deeper than stored depth 2)
        bool found = tt.TryProbe(key, depth: 4, alpha: -100, beta: 100, ply: 0,
            out _, out var bestFrom, out var bestTo, out bool hasCutoff);

        found.Should().BeTrue();
        hasCutoff.Should().BeFalse("Stored entry is too shallow to cause a score cutoff");
        bestFrom.Should().Be(from);
        bestTo.Should().Be(to);
    }

    [Fact]
    public void MateScore_NormalizesWithPly()
    {
        int winScore = 95_000;
        int plyStored = 2;
        int plyProbed = 4;

        int stored = TranspositionTable.ScoreToTt(winScore, plyStored);
        int retrieved = TranspositionTable.ScoreFromTt(stored, plyProbed);

        // Win score should correctly adjust relative to probing ply
        retrieved.Should().Be(winScore + plyStored - plyProbed);
    }

    [Fact]
    public void NewSearch_IncrementsAgeAndAllowsReplacement()
    {
        var tt = new TranspositionTable(megabytes: 1);
        ulong key1 = 0x1111111111111111;

        tt.Store(key1, depth: 5, score: 100, bound: TranspositionBound.Exact, ply: 0);

        tt.NewSearch();

        // Storing shallower entry from new search should replace older search entry
        tt.Store(key1, depth: 2, score: 200, bound: TranspositionBound.Exact, ply: 0);

        tt.TryProbe(key1, depth: 2, alpha: -1000, beta: 1000, ply: 0,
            out int score, out _, out _, out bool cutoff);

        cutoff.Should().BeTrue();
        score.Should().Be(200);
    }

    [Fact]
    public void Clear_EmptiesAllEntriesAndResetsStats()
    {
        var tt = new TranspositionTable(megabytes: 1);
        ulong key = 0x12345;
        tt.Store(key, depth: 3, score: 50, bound: TranspositionBound.Exact, ply: 0);

        tt.Clear();

        tt.OccupiedEntries.Should().Be(0);
        tt.Probes.Should().Be(0);
        tt.Hits.Should().Be(0);
        tt.TryProbe(key, depth: 3, alpha: -100, beta: 100, ply: 0,
            out _, out _, out _, out bool cutoff);
        cutoff.Should().BeFalse();
    }

    [Fact]
    public void TranspositionEntry_IsCompact16Bytes()
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<TranspositionEntry>();
        size.Should().Be(16);
    }

    [Fact]
    public void FromEntries_DefaultAndMaxCapacities_AllocatesExpectedEntries()
    {
        var defaultTt = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
        defaultTt.Capacity.Should().Be(1_048_576);
        defaultTt.BucketCount.Should().Be(262_144);

        var maxTt = TranspositionTable.FromEntries(TranspositionTable.MaxEntries);
        maxTt.Capacity.Should().Be(16_777_216);
        maxTt.BucketCount.Should().Be(4_194_304);
    }

    [Fact]
    public void FourWayBucket_StoresFourDistinctKeysInSameBucketWithoutCollision()
    {
        var tt = TranspositionTable.FromEntries(1024); // 256 buckets
        ulong bucketIndex = 42UL;

        // 4 distinct keys sharing the exact same lower bits (same bucket) but different upper 32 bits
        ulong k1 = (0x11111111UL << 32) | bucketIndex;
        ulong k2 = (0x22222222UL << 32) | bucketIndex;
        ulong k3 = (0x33333333UL << 32) | bucketIndex;
        ulong k4 = (0x44444444UL << 32) | bucketIndex;

        tt.Store(k1, depth: 3, score: 10, bound: TranspositionBound.Exact, ply: 0);
        tt.Store(k2, depth: 4, score: 20, bound: TranspositionBound.Exact, ply: 0);
        tt.Store(k3, depth: 5, score: 30, bound: TranspositionBound.Exact, ply: 0);
        tt.Store(k4, depth: 6, score: 40, bound: TranspositionBound.Exact, ply: 0);

        tt.OccupiedEntries.Should().Be(4);

        tt.TryProbe(k1, 3, -100, 100, 0, out int s1, out _, out _, out bool c1).Should().BeTrue();
        tt.TryProbe(k2, 4, -100, 100, 0, out int s2, out _, out _, out bool c2).Should().BeTrue();
        tt.TryProbe(k3, 5, -100, 100, 0, out int s3, out _, out _, out bool c3).Should().BeTrue();
        tt.TryProbe(k4, 6, -100, 100, 0, out int s4, out _, out _, out bool c4).Should().BeTrue();

        (c1 && c2 && c3 && c4).Should().BeTrue();
        s1.Should().Be(10);
        s2.Should().Be(20);
        s3.Should().Be(30);
        s4.Should().Be(40);
        tt.Collisions.Should().Be(0);
    }

    [Fact]
    public void FourWayBucket_FifthKeyEvictsLowestPriorityVictim()
    {
        var tt = TranspositionTable.FromEntries(1024);
        ulong bucketIndex = 77UL;

        ulong k1 = (0x10000001UL << 32) | bucketIndex;
        ulong k2 = (0x20000002UL << 32) | bucketIndex;
        ulong k3 = (0x30000003UL << 32) | bucketIndex;
        ulong k4 = (0x40000004UL << 32) | bucketIndex;
        ulong k5 = (0x50000005UL << 32) | bucketIndex;

        // k2 has the shallowest depth (depth 1, UpperBound) -> lowest priority
        tt.Store(k1, depth: 6, score: 10, bound: TranspositionBound.Exact, ply: 0);
        tt.Store(k2, depth: 1, score: 20, bound: TranspositionBound.UpperBound, ply: 0);
        tt.Store(k3, depth: 5, score: 30, bound: TranspositionBound.LowerBound, ply: 0);
        tt.Store(k4, depth: 4, score: 40, bound: TranspositionBound.Exact, ply: 0);

        // Store 5th key into the full bucket -> should evict k2
        tt.Store(k5, depth: 3, score: 50, bound: TranspositionBound.Exact, ply: 0);

        tt.TryProbe(k2, 1, -100, 100, 0, out _, out _, out _, out _).Should().BeFalse("k2 had the lowest priority and should be evicted");
        tt.TryProbe(k1, 6, -100, 100, 0, out int s1, out _, out _, out _).Should().BeTrue();
        tt.TryProbe(k3, 5, -100, 100, 0, out _, out _, out _, out _).Should().BeTrue();
        tt.TryProbe(k4, 4, -100, 100, 0, out _, out _, out _, out _).Should().BeTrue();
        tt.TryProbe(k5, 3, -100, 100, 0, out int s5, out _, out _, out _).Should().BeTrue();

        s1.Should().Be(10);
        s5.Should().Be(50);
    }

    [Fact]
    public void StoreAndProbe_CachesAndPreservesStaticEval()
    {
        var tt = TranspositionTable.FromEntries(1024);
        ulong key = 0xABCDEF0123456789UL;
        ushort packedMove = (ushort)((40 << 8) | 33);

        tt.Store(key, depth: 2, score: 45, bound: TranspositionBound.LowerBound, ply: 1, packedMove, hasBestMove: true, staticEval: 38);

        // Update same key at deeper depth without passing staticEval -> should preserve cached staticEval (38)
        tt.Store(key, depth: 5, score: 62, bound: TranspositionBound.Exact, ply: 1, bestPackedMove: 0, hasBestMove: false);

        bool found = tt.TryProbe(
            key,
            depth: 5,
            alpha: -100,
            beta: 100,
            ply: 1,
            out int score,
            out ushort bestMoveOut,
            out bool hasMoveOut,
            out int staticEvalOut,
            out bool hasStaticEvalOut,
            out bool hasCutoff);

        found.Should().BeTrue();
        hasCutoff.Should().BeTrue();
        score.Should().Be(62);
        hasMoveOut.Should().BeTrue("Existing best move should be preserved when new store has no move");
        bestMoveOut.Should().Be(packedMove);
        hasStaticEvalOut.Should().BeTrue("Existing staticEval should be preserved");
        staticEvalOut.Should().Be(38);
    }
}
