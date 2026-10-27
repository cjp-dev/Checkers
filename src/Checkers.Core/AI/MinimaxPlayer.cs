using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Checkers.Core.AI.Book;
using Checkers.Core.Bitboards;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Represents one of the top-<c>k</c> moves found by <see cref="MinimaxPlayer.SearchMultiPv"/>,
/// along with its exact centipawn score and the completed search depth in plies.
/// </summary>
public readonly record struct MultiPvMoveResult(Move Move, int Score, int CompletedDepth);

/// <summary>
/// High-performance 64-bit Bitboard Minimax AI with Alpha-Beta pruning, iterative deepening, PV and TT move ordering,
/// 64-bit Zobrist transposition table, quiescence search, live progress reporting, and time controls.
/// Uses value-type <see cref="BitPosition"/> copy-make and preallocated per-ply <see cref="BitMove"/> buffers.
/// </summary>
public sealed class MinimaxPlayer : IPlayer
{
    internal const int WinScore = 30_000;
    internal const int LossScore = -30_000;

    /// <summary>Upper limit for <see cref="Threads"/>.</summary>
    public const int MaxThreads = 64;
    private const int DominantMoveMinDepth = 8;
    private const int DominantMoveMargin = 150;
    private const int DominantMoveRequiredStreak = 2;

    private readonly CheckersVariant _variant;
    private readonly IEvaluationFunction? _customEvaluator;

    // Worker 0 is the main search; workers 1.. are the Lazy SMP helpers, created on first use.
    private readonly List<SearchWorker> _workers = [];
    private int _activeWorkers = 1;

    public string Name { get; }
    public SearchLimits Limits { get; }
    public int Depth => Limits.Depth;
    /// <summary>Nodes searched by all threads in the last search (live while a search runs).</summary>
    public long NodesEvaluated
    {
        get
        {
            long total = 0;
            for (int i = 0; i < _activeWorkers; i++)
            {
                total += Volatile.Read(ref _workers[i].Nodes);
            }
            return total;
        }
    }

    /// <summary>Static evaluations made by all threads in the last search.</summary>
    public long LeafEvaluations
    {
        get
        {
            long total = 0;
            for (int i = 0; i < _activeWorkers; i++)
            {
                total += Volatile.Read(ref _workers[i].Leaves);
            }
            return total;
        }
    }
    public SearchAnalysis? LastAnalysis { get; private set; }

    public TranspositionTable? TranspositionTable { get; }
    public bool UseTranspositionTable => TranspositionTable != null;
    public OpeningBook? OpeningBook { get; set; }
    public bool UseOpeningBook { get; set; }
    public Random BookRandom { get; set; } = Random.Shared;
    public bool UseQuiescence { get; set; } = true;
    public bool UseSelectivePruning { get; set; } = true;
    public bool UseEarlyRootTermination { get; set; } = true;

    /// <summary>
    /// Search threads (Lazy SMP): the main thread plus <c>Threads - 1</c> helpers that search the same root
    /// and share the transposition table. 1 (the default) is the plain single-threaded search.
    /// Ignored (1 thread) with a custom evaluation function, which may not be thread-safe, without a
    /// transposition table, and by <see cref="SearchMultiPv"/>.
    /// </summary>
    public int Threads
    {
        get => _threads;
        set => _threads = Math.Clamp(value, 1, MaxThreads);
    }
    private int _threads = 1;
    public bool LastSearchAdoptedPartialIteration { get; private set; }
    public bool LastSearchTerminatedEarly { get; private set; }

    public MinimaxPlayer(
        SearchLimits limits,
        string? name = null,
        IRuleEngine? ruleEngine = null,
        IEvaluationFunction? evaluator = null,
        TranspositionTable? transpositionTable = null,
        bool useTranspositionTable = true,
        bool useQuiescence = true,
        CheckersVariant? variant = null,
        OpeningBook? openingBook = null)
    {
        Limits = limits;
        _variant = variant
            ?? ruleEngine?.Variant
            ?? (evaluator as EvaluationFunction)?.Variant
            ?? CheckersVariant.International;
        _customEvaluator = evaluator is null or EvaluationFunction ? null : evaluator;
        Name = name ?? limits.Mode switch
        {
            TimeControlMode.FixedDepth => $"Minimax (Depth {limits.Depth})",
            TimeControlMode.TimePerMove => $"Minimax ({limits.Time.TotalSeconds:0}s/move)",
            TimeControlMode.TimePerGame => "Minimax (Clock)",
            _ => "Minimax AI"
        };
        TranspositionTable = useTranspositionTable
            ? (transpositionTable ?? new TranspositionTable(megabytes: 16))
            : null;
        UseQuiescence = useQuiescence;
        OpeningBook = openingBook;
        UseOpeningBook = openingBook != null || limits.UseOpeningBook;

        _workers.Add(CreateWorker());
    }

    private SearchWorker CreateWorker() => new(this, _variant, _customEvaluator, TranspositionTable);

    public MinimaxPlayer(
        int depth = 4,
        string? name = null,
        IRuleEngine? ruleEngine = null,
        IEvaluationFunction? evaluator = null,
        TranspositionTable? transpositionTable = null,
        bool useTranspositionTable = true,
        bool useQuiescence = true,
        CheckersVariant? variant = null,
        OpeningBook? openingBook = null)
        : this(SearchLimits.FixedDepth(depth, useOpeningBook: openingBook != null), name, ruleEngine, evaluator, transpositionTable, useTranspositionTable, useQuiescence, variant, openingBook)
    {
    }

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, progress: null, stateHashHistory: null, cancellationToken, moveNowToken: default);

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, progress, stateHashHistory: null, cancellationToken, moveNowToken: default);

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        IReadOnlyList<ulong>? stateHashHistory,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, progress, stateHashHistory, cancellationToken, moveNowToken: default);

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        IReadOnlyList<ulong>? stateHashHistory,
        CancellationToken cancellationToken,
        CancellationToken moveNowToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSearchAdoptedPartialIteration = false;
        LastSearchTerminatedEarly = false;

        if (legalMoves.Count == 0)
            throw new InvalidOperationException("No legal moves available.");

        if (legalMoves.Count == 1)
        {
            var singleMove = legalMoves[0];
            LastSearchTerminatedEarly = true;
            LastAnalysis = new SearchAnalysis
            {
                Move = singleMove.Notation,
                Depth = "1 ply (Forced)",
                Score = "Forced",
                BestMove = singleMove.Notation,
                Nodes = "1",
                Speed = "-",
                Time = "0:00.0"
            };
            progress?.Report(LastAnalysis);
            return ValueTask.FromResult(singleMove);
        }

        if (UseOpeningBook)
        {
            var activeBook = OpeningBook ?? OpeningBook.GetDefault(_variant);
            if (activeBook.Count > 0 &&
                activeBook.TryGetMove(state, legalMoves, BookRandom, Limits.BookRandomMarginCp, out var bookMove, out int bookScore))
            {
                ResetNodeCounters();
                LastSearchTerminatedEarly = true;
                LastAnalysis = new SearchAnalysis
                {
                    Move = bookMove.Notation,
                    Depth = $"Book ({activeBook.Depth} plies)",
                    Score = FormatValue(bookScore),
                    BestMove = bookMove.Notation,
                    Nodes = "0 (Book)",
                    Speed = "-",
                    Time = "0:00.0",
                    FromBook = true
                };
                progress?.Report(LastAnalysis);
                return ValueTask.FromResult(bookMove);
            }
        }

        var sw = Stopwatch.StartNew();
        TranspositionTable?.NewSearch();

        long softLimitMs = long.MaxValue;
        long hardLimitMs = long.MaxValue;
        bool isTimedSearch = Limits.Mode != TimeControlMode.FixedDepth;
        int maxTargetDepth = isTimedSearch ? 48 : Limits.Depth;

        if (Limits.Mode == TimeControlMode.TimePerMove)
        {
            hardLimitMs = (long)Limits.Time.TotalMilliseconds;
            softLimitMs = (hardLimitMs * 2) / 3;
        }
        else if (Limits.Mode == TimeControlMode.TimePerGame)
        {
            int piecesLeft = state.WhitePiecesCount + state.BlackPiecesCount;
            int estimatedMoves = Math.Clamp(piecesLeft, 2, 25);
            long allocatedMs = (long)(Limits.Time.TotalMilliseconds / estimatedMoves);
            hardLimitMs = Math.Max(10, allocatedMs);
            softLimitMs = (hardLimitMs * 2) / 3;
        }

        BitPosition rootPos = state.BitPosition;
        SearchWorker main = _workers[0];
        _activeWorkers = 1;
        main.BeginSearch(in rootPos, stateHashHistory, sw, hardLimitMs, cancellationToken, moveNowToken, progress, stop: null);

        var currentOrder = legalMoves
            .OrderByDescending(m => m.CapturedPositions.Count)
            .ThenByDescending(m => m.IsPromotion ? 1 : 0)
            .Select(m => (Move: m, BitMove: BitMove.FromMove(m)))
            .ToList();

        var stop = new SearchStop();
        Task[]? helpers = StartHelpers(in rootPos, stateHashHistory, currentOrder, maxTargetDepth, stop);

        Move? bestMoveOverall = null;
        int bestScoreOverall = 0;
        int completedDepth = 1;
        ushort lastCompletedBestPacked = 0;
        int dominantMoveStreak = 0;

        try
        {
            for (int d = 1; d <= maxTargetDepth; d++)
            {
                if (d > 1 && sw.ElapsedMilliseconds >= softLimitMs)
                    break;

                (Move Move, BitMove BitMove)? bestEntryThisDepth = null;
                int bestScoreThisDepth = int.MinValue;
                int secondBestScoreThisDepth = int.MinValue;
                int alpha = -200_000;
                int beta = 200_000;
                bool depthAborted = false;

                for (int i = 0; i < currentOrder.Count; i++)
                {
                    var entry = currentOrder[i];
                    cancellationToken.ThrowIfCancellationRequested();
                    if (moveNowToken.IsCancellationRequested || sw.ElapsedMilliseconds >= hardLimitMs)
                    {
                        depthAborted = true;
                        break;
                    }

                    BitPosition nextPos = rootPos.Apply(in entry.BitMove);
                    TranspositionTable?.Prefetch(nextPos.Hash);
                    main.CurrentIterDepth = d;
                    main.CurrentRootMove = entry.Move;
                    main.BestRootMoveOverall = bestMoveOverall;
                    main.BestScoreOverall = bestScoreOverall;
                    int score = -main.NegaMax(in nextPos, d - 1, -beta, -alpha, ply: 1, out bool aborted);

                    if (aborted)
                    {
                        depthAborted = true;
                        break;
                    }

                    if (score > bestScoreThisDepth)
                    {
                        secondBestScoreThisDepth = bestScoreThisDepth;
                        bestScoreThisDepth = score;
                        bestEntryThisDepth = entry;

                        // 5.1 Partial-Iteration Root Move Adoption on Timeout:
                        // currentOrder[0] is always the previous iteration's best move. Once i == 0 finishes
                        // cleanly at depth d (or a later root move i > 0 finishes cleanly and beats it at depth d),
                        // publish it immediately so a timeout on a later root move preserves the deeper result.
                        bestMoveOverall = entry.Move;
                        bestScoreOverall = score;
                    }
                    else if (score > secondBestScoreThisDepth)
                    {
                        secondBestScoreThisDepth = score;
                    }

                    if (score > alpha)
                    {
                        alpha = score;
                    }

                    if (progress != null && sw.ElapsedMilliseconds - main.LastReportMs >= 100)
                    {
                        main.LastReportMs = sw.ElapsedMilliseconds;
                        var heartbeatAnalysis = new SearchAnalysis
                        {
                            Move = entry.Move.Notation,
                            Depth = $"{d} plies...",
                            Score = FormatValue(bestScoreThisDepth > int.MinValue ? bestScoreThisDepth : bestScoreOverall),
                            BestMove = bestEntryThisDepth?.Move.Notation ?? bestMoveOverall?.Notation ?? entry.Move.Notation,
                            Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
                            Speed = FormatSpeed(NodesEvaluated, sw.ElapsedMilliseconds),
                            Time = sw.Elapsed.ToString(@"m\:ss\.f")
                        };
                        progress.Report(heartbeatAnalysis);
                    }
                }

                if (!depthAborted && bestEntryThisDepth.HasValue)
                {
                    bestMoveOverall = bestEntryThisDepth.Value.Move;
                    bestScoreOverall = bestScoreThisDepth;
                    completedDepth = d;
                    LastSearchAdoptedPartialIteration = false;

                    ushort currentBestPacked = bestEntryThisDepth.Value.BitMove.PackedMove;
                    if (secondBestScoreThisDepth > int.MinValue &&
                        bestScoreThisDepth - secondBestScoreThisDepth >= DominantMoveMargin)
                    {
                        dominantMoveStreak = currentBestPacked == lastCompletedBestPacked
                            ? dominantMoveStreak + 1
                            : 1;
                    }
                    else
                    {
                        dominantMoveStreak = 0;
                    }
                    lastCompletedBestPacked = currentBestPacked;

                    currentOrder.Remove(bestEntryThisDepth.Value);
                    currentOrder.Insert(0, bestEntryThisDepth.Value);

                    var iterationAnalysis = new SearchAnalysis
                    {
                        Move = bestMoveOverall.Notation,
                        Depth = $"{completedDepth} plies",
                        Score = FormatValue(bestScoreOverall),
                        BestMove = bestMoveOverall.Notation,
                        Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
                        Speed = FormatSpeed(NodesEvaluated, sw.ElapsedMilliseconds),
                        Time = sw.Elapsed.ToString(@"m\:ss\.f")
                    };
                    LastAnalysis = iterationAnalysis;
                    main.LastReportMs = sw.ElapsedMilliseconds;
                    progress?.Report(iterationAnalysis);

                    if (bestScoreOverall >= TranspositionTable.WinThreshold)
                        break;

                    // 5.2 Early Root Termination on Single Viable Move (Dominant Move Exit):
                    // In timed searches at depth >= 8, if the same root move leads all alternatives by >= 150 cp
                    // (1.5 men) for 2 consecutive completed iterations (or all moves are proven forced losses),
                    // terminate early to save clock time.
                    if (UseEarlyRootTermination && isTimedSearch && d < maxTargetDepth)
                    {
                        if (bestScoreOverall <= -TranspositionTable.WinThreshold ||
                            (d >= DominantMoveMinDepth && dominantMoveStreak >= DominantMoveRequiredStreak))
                        {
                            LastSearchTerminatedEarly = true;
                            break;
                        }
                    }
                }
                else
                {
                    if (bestEntryThisDepth.HasValue)
                    {
                        LastSearchAdoptedPartialIteration = true;
                    }
                    break;
                }
            }
        }
        finally
        {
            StopHelpers(helpers, stop);
        }

        sw.Stop();

        bestMoveOverall ??= currentOrder[0].Move;

        LastAnalysis = new SearchAnalysis
        {
            Move = bestMoveOverall.Notation,
            Depth = $"{completedDepth} plies",
            Score = FormatValue(bestScoreOverall),
            BestMove = bestMoveOverall.Notation,
            Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
            Speed = FormatSpeed(NodesEvaluated, sw.ElapsedMilliseconds),
            Time = sw.Elapsed.ToString(@"m\:ss\.f")
        };

        progress?.Report(LastAnalysis);

        return ValueTask.FromResult(bestMoveOverall);
    }

    /// <summary>
    /// Runs an iterative-deepening Single-Search Multi-PV root search to find the top <paramref name="multiPvCount"/>
    /// moves and their exact centipawn scores from <paramref name="state"/>'s <c>SideToMove</c> perspective.
    /// Holds root <c>alpha</c> at the <c>k</c>-th best score found so far at each depth so that all top <c>k</c>
    /// moves receive exact minimax scores in a single search pass sharing the same <see cref="TranspositionTable"/>.
    /// </summary>
    public IReadOnlyList<MultiPvMoveResult> SearchMultiPv(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        int multiPvCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSearchAdoptedPartialIteration = false;
        LastSearchTerminatedEarly = false;

        if (legalMoves.Count == 0)
        {
            return [];
        }

        int targetK = Math.Clamp(multiPvCount, 1, legalMoves.Count);

        var sw = Stopwatch.StartNew();
        TranspositionTable?.NewSearch();

        bool isTimedSearch = Limits.Mode != TimeControlMode.FixedDepth;
        long hardLimitMs = isTimedSearch
            ? Math.Max(1, (long)Limits.Time.TotalMilliseconds)
            : long.MaxValue;
        int maxTargetDepth = isTimedSearch ? 48 : Math.Max(1, Limits.Depth);

        BitPosition rootPos = state.BitPosition;
        SearchWorker main = _workers[0];
        _activeWorkers = 1;
        main.BeginSearch(in rootPos, stateHashHistory: null, sw, hardLimitMs, cancellationToken, moveNowToken: default, progress: null, stop: null);

        var currentOrder = legalMoves
            .OrderByDescending(m => m.CapturedPositions.Count)
            .ThenByDescending(m => m.IsPromotion ? 1 : 0)
            .ThenBy(m => m.Notation, StringComparer.Ordinal)
            .Select(m => (Move: m, BitMove: BitMove.FromMove(m), Score: 0))
            .ToList();

        List<MultiPvMoveResult>? lastCompletedTopK = null;
        int completedDepth = 0;

        for (int d = 1; d <= maxTargetDepth; d++)
        {
            if (d > 1 && sw.ElapsedMilliseconds >= hardLimitMs)
            {
                break;
            }

            var scoredThisDepth = new List<(Move Move, BitMove BitMove, int Score)>(currentOrder.Count);
            var topKScores = new List<int>(targetK);
            bool depthAborted = false;

            for (int i = 0; i < currentOrder.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sw.ElapsedMilliseconds >= hardLimitMs)
                {
                    depthAborted = true;
                    break;
                }

                int alpha = topKScores.Count < targetK ? -200_000 : topKScores[targetK - 1];
                int beta = 200_000;
                var entry = currentOrder[i];

                BitPosition nextPos = rootPos.Apply(in entry.BitMove);
                TranspositionTable?.Prefetch(nextPos.Hash);
                int score = -main.NegaMax(in nextPos, d - 1, -beta, -alpha, ply: 1, out bool aborted);

                if (aborted)
                {
                    depthAborted = true;
                    break;
                }

                scoredThisDepth.Add((entry.Move, entry.BitMove, score));

                int insertIdx = 0;
                while (insertIdx < topKScores.Count && topKScores[insertIdx] >= score)
                {
                    insertIdx++;
                }

                if (insertIdx < targetK)
                {
                    topKScores.Insert(insertIdx, score);
                    if (topKScores.Count > targetK)
                    {
                        topKScores.RemoveAt(targetK);
                    }
                }
            }

            if (!depthAborted && scoredThisDepth.Count > 0)
            {
                currentOrder = scoredThisDepth
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Move.Notation, StringComparer.Ordinal)
                    .ToList();

                completedDepth = d;
                lastCompletedTopK = currentOrder
                    .Take(targetK)
                    .Select(x => new MultiPvMoveResult(
                        x.Move,
                        Math.Clamp(x.Score, BookFile.MinScore, BookFile.MaxScore),
                        d))
                    .ToList();

                if (topKScores.Count == targetK &&
                    (topKScores[targetK - 1] >= TranspositionTable.WinThreshold ||
                     topKScores[0] <= -TranspositionTable.WinThreshold))
                {
                    break;
                }
            }
            else
            {
                if (scoredThisDepth.Count >= targetK)
                {
                    LastSearchAdoptedPartialIteration = true;
                    completedDepth = d;
                    lastCompletedTopK = scoredThisDepth
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.Move.Notation, StringComparer.Ordinal)
                        .Take(targetK)
                        .Select(x => new MultiPvMoveResult(
                            x.Move,
                            Math.Clamp(x.Score, BookFile.MinScore, BookFile.MaxScore),
                            d))
                        .ToList();
                }

                break;
            }
        }

        sw.Stop();

        if (lastCompletedTopK == null || lastCompletedTopK.Count == 0)
        {
            lastCompletedTopK = currentOrder
                .Select(x =>
                {
                    BitPosition nextPos = rootPos.Apply(in x.BitMove);
                    int staticScore = Math.Clamp(-main.EvaluatePosition(in nextPos), BookFile.MinScore, BookFile.MaxScore);
                    return new MultiPvMoveResult(x.Move, staticScore, 1);
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Move.Notation, StringComparer.Ordinal)
                .Take(targetK)
                .ToList();
            completedDepth = 1;
        }

        var best = lastCompletedTopK[0];
        LastAnalysis = new SearchAnalysis
        {
            Move = best.Move.Notation,
            Depth = $"{completedDepth} plies",
            Score = FormatValue(best.Score),
            BestMove = best.Move.Notation,
            Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
            Speed = FormatSpeed(NodesEvaluated, sw.ElapsedMilliseconds),
            Time = sw.Elapsed.ToString(@"m\:ss\.f")
        };

        return lastCompletedTopK;
    }

    private void ResetNodeCounters()
    {
        for (int i = 0; i < _workers.Count; i++)
        {
            _workers[i].Nodes = 0;
            _workers[i].Leaves = 0;
        }
    }

    /// <summary>
    /// Starts the Lazy SMP helper threads (none when <see cref="Threads"/> is 1). All workers are prepared
    /// before the first helper starts. Odd helpers start one ply deeper than the main thread, and each helper
    /// searches the root moves after the first in a rotated order, so the threads spread over different subtrees.
    /// </summary>
    private Task[]? StartHelpers(
        in BitPosition rootPos,
        IReadOnlyList<ulong>? stateHashHistory,
        List<(Move Move, BitMove BitMove)> rootOrder,
        int maxTargetDepth,
        SearchStop stop)
    {
        int threads = _customEvaluator == null && TranspositionTable != null ? _threads : 1;
        if (threads <= 1 || rootOrder.Count < 2)
        {
            return null;
        }

        while (_workers.Count < threads)
        {
            _workers.Add(CreateWorker());
        }

        var unused = new Stopwatch();
        for (int id = 1; id < threads; id++)
        {
            _workers[id].BeginSearch(in rootPos, stateHashHistory, unused, long.MaxValue, default, default, progress: null, stop);
        }
        _activeWorkers = threads;

        var helpers = new List<Task>(threads - 1);
        BitPosition root = rootPos;
        try
        {
            for (int id = 1; id < threads; id++)
            {
                SearchWorker worker = _workers[id];
                BitMove[] moves = RotatedRootMoves(rootOrder, id);
                int startDepth = Math.Min(1 + (id & 1), maxTargetDepth);
                helpers.Add(Task.Factory.StartNew(
                    () => worker.RunHelper(root, moves, startDepth, maxTargetDepth),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default));
            }
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or NotSupportedException)
        {
            // No threads on this platform (single-threaded WebAssembly): search with the helpers started so far.
        }

        return helpers.ToArray();
    }

    private static BitMove[] RotatedRootMoves(List<(Move Move, BitMove BitMove)> rootOrder, int helperId)
    {
        int count = rootOrder.Count;
        var moves = new BitMove[count];
        moves[0] = rootOrder[0].BitMove;
        int shift = helperId % (count - 1);
        for (int i = 1; i < count; i++)
        {
            moves[i] = rootOrder[1 + ((i - 1 + shift) % (count - 1))].BitMove;
        }
        return moves;
    }

    private static void StopHelpers(Task[]? helpers, SearchStop stop)
    {
        if (helpers == null)
        {
            return;
        }

        stop.Requested = true;
        Task.WaitAll(helpers);
    }

    internal static string FormatSpeed(long nodes, long elapsedMs)
    {
        if (elapsedMs <= 0 || nodes <= 0)
            return "-";
        long nps = (nodes * 1000L) / elapsedMs;
        return $"{nps.ToString("N0", CultureInfo.InvariantCulture)} nps";
    }

    internal static string FormatValue(int score) => score switch
    {
        >= TranspositionTable.WinThreshold => $"+Win in {WinScore - score} plies",
        <= -TranspositionTable.WinThreshold => $"-Loss in {score - LossScore} plies",
        > 0 => $"+{score}",
        _ => score.ToString()
    };
}
