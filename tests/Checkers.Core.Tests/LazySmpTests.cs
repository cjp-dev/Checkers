using System.Collections.Concurrent;
using Checkers.Core.AI;
using Checkers.Core.AI.Benchmark;
using Checkers.Core.Bitboards;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class LazySmpTests
{
    [Fact]
    public void TranspositionTable_ConcurrentStoreAndProbe_NeverReturnsAMixOfTwoEntries()
    {
        // A tiny table (256 buckets) so that 8 threads constantly overwrite each other's slots.
        var tt = TranspositionTable.FromEntries(1024);
        const int keysPerThread = 200_000;
        int mismatches = 0;
        long hits = 0;

        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, thread =>
        {
            var rng = new Random(thread);
            for (int i = 0; i < keysPerThread; i++)
            {
                // Keys come from a small shared pool so that threads also store and probe the same keys.
                ulong key = Mix((ulong)rng.Next(4096));
                (int depth, int score, ushort move, int eval) = Expected(key);

                tt.Store(key, depth, score, TranspositionBound.Exact, ply: 0, move, hasBestMove: true, eval);

                ulong probeKey = Mix((ulong)rng.Next(4096));
                if (tt.TryProbe(probeKey, depth: 0, alpha: -1000, beta: 1000, ply: 0,
                        out int gotScore, out ushort gotMove, out bool gotHasMove, out int gotEval, out bool gotHasEval, out bool cutoff))
                {
                    Interlocked.Increment(ref hits);
                    (_, int expScore, ushort expMove, int expEval) = Expected(probeKey);
                    if (!cutoff || gotScore != expScore || !gotHasMove || gotMove != expMove || !gotHasEval || gotEval != expEval)
                    {
                        Interlocked.Increment(ref mismatches);
                    }
                }
            }
        });

        hits.Should().BeGreaterThan(1000);
        mismatches.Should().Be(0);

        static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        // Every field is a function of the key, so any entry made of two different stores is detectable.
        static (int Depth, int Score, ushort Move, int Eval) Expected(ulong key) =>
            ((int)(key % 20), (int)(key >> 20) % 900, (ushort)(key >> 8), (int)(key >> 40) % 400);
    }

    [Theory]
    [InlineData(CheckersVariant.International, 26, 2)]
    [InlineData(CheckersVariant.International, 31, 8)]
    [InlineData(CheckersVariant.International, 35, 4)]
    [InlineData(CheckersVariant.English, 27, 4)]
    [InlineData(CheckersVariant.English, 35, 8)]
    public async Task MultiThreadedSearch_FindsTheClearlyBestMove(CheckersVariant variant, int positionId, int threads)
    {
        // Benchmark positions where, at depth 10, the best move leads the second best by more than 120 cp
        // (exact scores from SearchMultiPv, which never prunes the second move away).
        const int depth = 10;
        var state = BenchmarkSuite.GetPositions().Single(p => p.Id == positionId).State;
        var rules = new RuleEngine(variant);
        var legalMoves = rules.GetLegalMoves(state);

        var top = new MinimaxPlayer(SearchLimits.FixedDepth(depth), ruleEngine: rules, variant: variant)
            .SearchMultiPv(state, legalMoves, multiPvCount: 2);
        (top[0].Score - top[1].Score).Should().BeGreaterThan(120);

        var smp = new MinimaxPlayer(SearchLimits.FixedDepth(depth), ruleEngine: rules, variant: variant)
        {
            Threads = threads
        };
        var move = await smp.GetMoveAsync(state, legalMoves);

        move.Notation.Should().Be(top[0].Move.Notation);
    }

    [Fact]
    public async Task MultiThreadedSearch_ReportsTheNodesOfAllThreads()
    {
        var board = BoardState.CreateInitial();
        var rules = new RuleEngine();
        var legalMoves = rules.GetLegalMoves(board);

        var smp = new MinimaxPlayer(SearchLimits.FixedDepth(9)) { Threads = 4 };
        var move = await smp.GetMoveAsync(board, legalMoves);

        legalMoves.Should().Contain(move);
        smp.NodesEvaluated.Should().BeGreaterThan(0);
        smp.LastAnalysis!.Nodes.Should().Be(smp.NodesEvaluated.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task MultiThreadedSearch_MoveNow_ReturnsPromptlyAndThePlayerCanSearchAgain()
    {
        var board = BoardState.CreateInitial();
        var rules = new RuleEngine();
        var legalMoves = rules.GetLegalMoves(board);
        var tt = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
        var player = new MinimaxPlayer(SearchLimits.FixedDepth(40), transpositionTable: tt) { Threads = 4 };

        using var moveNow = new CancellationTokenSource();
        moveNow.CancelAfter(100);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var move = await player.GetMoveAsync(board, legalMoves, progress: null, stateHashHistory: null, CancellationToken.None, moveNow.Token);
        sw.Stop();

        legalMoves.Should().Contain(move);
        sw.ElapsedMilliseconds.Should().BeLessThan(2000);

        // The helpers are gone and the shared table is still usable.
        var again = new MinimaxPlayer(SearchLimits.FixedDepth(6), transpositionTable: tt) { Threads = 4 };
        legalMoves.Should().Contain(await again.GetMoveAsync(board, legalMoves));
    }

    [Fact]
    public async Task MultiThreadedSearch_Cancel_ThrowsAndThePlayerCanSearchAgain()
    {
        var board = BoardState.CreateInitial();
        var rules = new RuleEngine();
        var legalMoves = rules.GetLegalMoves(board);
        var player = new MinimaxPlayer(SearchLimits.FixedDepth(40)) { Threads = 4 };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);
        var act = async () => await player.GetMoveAsync(board, legalMoves, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var again = new MinimaxPlayer(SearchLimits.FixedDepth(6), transpositionTable: player.TranspositionTable) { Threads = 4 };
        legalMoves.Should().Contain(await again.GetMoveAsync(board, legalMoves));
    }

    [Fact]
    public async Task MultiThreadedSearch_TimePerMove_StopsAtTheHardLimit()
    {
        var board = BoardState.CreateInitial();
        var rules = new RuleEngine();
        var legalMoves = rules.GetLegalMoves(board);
        var player = new MinimaxPlayer(SearchLimits.TimePerMove(TimeSpan.FromMilliseconds(300))) { Threads = 4 };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var move = await player.GetMoveAsync(board, legalMoves);
        sw.Stop();

        legalMoves.Should().Contain(move);
        sw.ElapsedMilliseconds.Should().BeLessThan(1500);
    }

    [Fact]
    public async Task CustomEvaluator_IsOnlyCalledFromOneThread()
    {
        var board = BoardState.CreateInitial();
        var rules = new RuleEngine();
        var legalMoves = rules.GetLegalMoves(board);
        var evaluator = new ThreadRecordingEvaluator();
        var player = new MinimaxPlayer(SearchLimits.FixedDepth(7), evaluator: evaluator) { Threads = 4 };

        await player.GetMoveAsync(board, legalMoves);

        evaluator.ThreadIds.Should().ContainSingle();
    }

    private sealed class ThreadRecordingEvaluator : IEvaluationFunction
    {
        private readonly ConcurrentDictionary<int, bool> _threads = new();

        public ICollection<int> ThreadIds => _threads.Keys;

        public int Evaluate(BoardState state) => 0;

        public int Evaluate(in BitPosition pos)
        {
            _threads.TryAdd(Environment.CurrentManagedThreadId, true);
            return EvaluationFunction.Evaluate(in pos, CheckersVariant.International);
        }
    }
}
