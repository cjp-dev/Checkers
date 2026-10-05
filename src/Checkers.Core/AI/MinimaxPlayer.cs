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
    private const int WinScore = 30_000;
    private const int LossScore = -30_000;
    private const int MaxPly = 64;
    private const int MaxMovesPerNode = 128;

    private const int RfpMaxDepth = 6;
    private const int RfpMargin = 40;
    private const int FutilityMaxDepth = 3;
    private const int FutilityMargin = 60;
    private const int LmrMinDepth = 3;
    private const int LmrMinPly = 2;
    private const int LmrMinMoveIndex = 3;
    private const int LmrLateMoveIndex = 8;
    private const int DominantMoveMinDepth = 8;
    private const int DominantMoveMargin = 150;
    private const int DominantMoveRequiredStreak = 2;

    private readonly CheckersVariant _variant;
    private readonly IEvaluationFunction? _customEvaluator;
    private readonly BitMove[][] _moveBuffers;
    private readonly ushort[] _killers = new ushort[MaxPly * 2];
    private readonly int[] _history = new int[2 * 64 * 64];
    private readonly DrawTable _drawTable = new();

    public string Name { get; }
    public SearchLimits Limits { get; }
    public int Depth => Limits.Depth;
    public long NodesEvaluated { get; private set; }
    public long LeafEvaluations { get; private set; }
    public SearchAnalysis? LastAnalysis { get; private set; }

    public TranspositionTable? TranspositionTable { get; }
    public bool UseTranspositionTable => TranspositionTable != null;
    public OpeningBook? OpeningBook { get; set; }
    public bool UseOpeningBook { get; set; }
    public Random BookRandom { get; set; } = Random.Shared;
    public bool UseQuiescence { get; set; } = true;
    public bool UseSelectivePruning { get; set; } = true;
    public bool UseEarlyRootTermination { get; set; } = true;
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
            ? (transpositionTable ?? new TranspositionTable(megabytes: 32))
            : null;
        UseQuiescence = useQuiescence;
        OpeningBook = openingBook;
        UseOpeningBook = openingBook != null || limits.UseOpeningBook;

        _moveBuffers = new BitMove[MaxPly][];
        for (int i = 0; i < MaxPly; i++)
        {
            _moveBuffers[i] = new BitMove[MaxMovesPerNode];
        }
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EvaluatePosition(in BitPosition pos) =>
        _customEvaluator != null
            ? _customEvaluator.Evaluate(in pos)
            : EvaluationFunction.Evaluate(in pos, _variant);

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, progress: null, stateHashHistory: null, cancellationToken);

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, progress, stateHashHistory: null, cancellationToken);

    public async ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        IReadOnlyList<ulong>? stateHashHistory,
        CancellationToken cancellationToken = default)
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
                Value = "Forced",
                BestMove = singleMove.Notation,
                Nodes = "1",
                Evaluations = "0",
                Time = "0:00.0"
            };
            progress?.Report(LastAnalysis);
            if (progress != null)
            {
                await Task.Delay(1, cancellationToken);
            }
            return singleMove;
        }

        if (UseOpeningBook)
        {
            var activeBook = OpeningBook ?? OpeningBook.GetDefault(_variant);
            if (activeBook.Count > 0 &&
                activeBook.TryGetMove(state, legalMoves, BookRandom, Limits.BookRandomMarginCp, out var bookMove, out int bookScore))
            {
                NodesEvaluated = 0;
                LeafEvaluations = 0;
                LastSearchTerminatedEarly = true;
                LastAnalysis = new SearchAnalysis
                {
                    Move = bookMove.Notation,
                    Depth = $"Book ({activeBook.Depth} plies)",
                    Value = FormatValue(bookScore),
                    BestMove = bookMove.Notation,
                    Nodes = "0 (Book)",
                    Evaluations = "0",
                    Time = "0:00.0",
                    FromBook = true
                };
                progress?.Report(LastAnalysis);
                if (progress != null)
                {
                    await Task.Delay(1, cancellationToken);
                }
                return bookMove;
            }
        }

        var sw = Stopwatch.StartNew();
        NodesEvaluated = 0;
        LeafEvaluations = 0;
        long lastReportMs = 0;

        TranspositionTable?.NewSearch();
        Array.Clear(_killers);
        Array.Clear(_history);

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
        _drawTable.Reset(in rootPos, stateHashHistory);

        var currentOrder = legalMoves
            .OrderByDescending(m => m.CapturedPositions.Count)
            .ThenByDescending(m => m.IsPromotion ? 1 : 0)
            .Select(m => (Move: m, BitMove: BitMove.FromMove(m)))
            .ToList();

        Move? bestMoveOverall = null;
        int bestScoreOverall = 0;
        int completedDepth = 1;
        ushort lastCompletedBestPacked = 0;
        int dominantMoveStreak = 0;

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
                if (sw.ElapsedMilliseconds >= hardLimitMs)
                {
                    depthAborted = true;
                    break;
                }

                BitPosition nextPos = rootPos.Apply(in entry.BitMove);
                TranspositionTable?.Prefetch(nextPos.Hash);
                int score = -NegaMax(
                    in nextPos,
                    d - 1,
                    -beta,
                    -alpha,
                    ply: 1,
                    sw,
                    hardLimitMs,
                    cancellationToken,
                    progress,
                    ref lastReportMs,
                    d,
                    entry.Move,
                    bestMoveOverall,
                    bestScoreOverall,
                    out bool aborted);

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

                if (progress != null && sw.ElapsedMilliseconds - lastReportMs >= 150)
                {
                    lastReportMs = sw.ElapsedMilliseconds;
                    var heartbeatAnalysis = new SearchAnalysis
                    {
                        Move = entry.Move.Notation,
                        Depth = $"{d} plies...",
                        Value = FormatValue(bestScoreThisDepth > int.MinValue ? bestScoreThisDepth : bestScoreOverall),
                        BestMove = bestEntryThisDepth?.Move.Notation ?? bestMoveOverall?.Notation ?? entry.Move.Notation,
                        Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
                        Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
                        Time = sw.Elapsed.ToString(@"m\:ss\.f")
                    };
                    progress.Report(heartbeatAnalysis);
                    await Task.Delay(1, cancellationToken);
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
                    Value = FormatValue(bestScoreOverall),
                    BestMove = bestMoveOverall.Notation,
                    Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
                    Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
                    Time = sw.Elapsed.ToString(@"m\:ss\.f")
                };
                LastAnalysis = iterationAnalysis;
                lastReportMs = sw.ElapsedMilliseconds;
                progress?.Report(iterationAnalysis);

                if (progress != null)
                {
                    await Task.Delay(1, cancellationToken);
                }

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

        sw.Stop();

        bestMoveOverall ??= currentOrder[0].Move;

        LastAnalysis = new SearchAnalysis
        {
            Move = bestMoveOverall.Notation,
            Depth = $"{completedDepth} plies",
            Value = FormatValue(bestScoreOverall),
            BestMove = bestMoveOverall.Notation,
            Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
            Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
            Time = sw.Elapsed.ToString(@"m\:ss\.f")
        };

        progress?.Report(LastAnalysis);

        return bestMoveOverall;
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
        NodesEvaluated = 0;
        LeafEvaluations = 0;
        long lastReportMs = 0;

        TranspositionTable?.NewSearch();
        Array.Clear(_killers);
        Array.Clear(_history);

        bool isTimedSearch = Limits.Mode != TimeControlMode.FixedDepth;
        long hardLimitMs = isTimedSearch
            ? Math.Max(1, (long)Limits.Time.TotalMilliseconds)
            : long.MaxValue;
        int maxTargetDepth = isTimedSearch ? 48 : Math.Max(1, Limits.Depth);

        BitPosition rootPos = state.BitPosition;
        _drawTable.Reset(in rootPos, stateHashHistory: null);

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
                int score = -NegaMax(
                    in nextPos,
                    d - 1,
                    -beta,
                    -alpha,
                    ply: 1,
                    sw,
                    hardLimitMs,
                    cancellationToken,
                    progress: null,
                    ref lastReportMs,
                    d,
                    entry.Move,
                    currentOrder[0].Move,
                    currentOrder[0].Score,
                    out bool aborted);

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
                    int staticScore = Math.Clamp(-EvaluatePosition(in nextPos), BookFile.MinScore, BookFile.MaxScore);
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
            Value = FormatValue(best.Score),
            BestMove = best.Move.Notation,
            Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
            Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
            Time = sw.Elapsed.ToString(@"m\:ss\.f")
        };

        return lastCompletedTopK;
    }

    private int NegaMax(
        in BitPosition pos,
        int depth,
        int alpha,
        int beta,
        int ply,
        Stopwatch sw,
        long hardLimitMs,
        CancellationToken cancellationToken,
        IProgress<SearchAnalysis>? progress,
        ref long lastReportMs,
        int currentIterDepth,
        Move? currentRootMove,
        Move? bestRootMoveOverall,
        int bestScoreOverall,
        out bool aborted)
    {
        aborted = false;

        if ((NodesEvaluated & 4095) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sw.ElapsedMilliseconds >= hardLimitMs)
            {
                aborted = true;
                return 0;
            }

            if (progress != null && sw.ElapsedMilliseconds - lastReportMs >= 200)
            {
                lastReportMs = sw.ElapsedMilliseconds;
                progress.Report(new SearchAnalysis
                {
                    Move = currentRootMove?.Notation ?? bestRootMoveOverall?.Notation ?? "-",
                    Depth = $"{currentIterDepth} plies...",
                    Value = FormatValue(bestScoreOverall),
                    BestMove = bestRootMoveOverall?.Notation ?? "-",
                    Nodes = NodesEvaluated.ToString("N0", CultureInfo.InvariantCulture),
                    Evaluations = LeafEvaluations.ToString("N0", CultureInfo.InvariantCulture),
                    Time = sw.Elapsed.ToString(@"m\:ss\.f")
                });
            }
        }

        NodesEvaluated++;

        // Fast terminal status check matching RuleEngine.EvaluateGameStatus.
        // At depth > 0 with HalfMoveClock < 80, BitboardMoveGenerator.Generate below will catch 0 legal moves,
        // avoiding a redundant HasAnyLegalMove call on every interior node.
        ulong ownPieces = pos.SideToMove == PieceColor.White ? pos.White : pos.Black;
        if (ownPieces == 0UL || ((depth == 0 || pos.HalfMoveClock >= 80) && !BitboardMoveGenerator.HasAnyLegalMove(in pos, _variant)))
        {
            return LossScore + ply;
        }
        if (pos.HalfMoveClock >= 80)
        {
            return 0;
        }

        // In-search repetition check BEFORE probing the Transposition Table so cached
        // static/subtree scores never mask a repetition draw along the active search line.
        if (_drawTable.IsRepetition(in pos, ply))
        {
            return 0;
        }

        int originalAlpha = alpha;
        bool hasTtMove = false;
        ushort ttPackedMove = 0;
        int cachedStaticEval = TranspositionEntry.NoStaticEval;

        // 4-Way Bucket Transposition Table Probe
        if (TranspositionTable != null &&
            TranspositionTable.TryProbe(
                pos.Hash,
                depth,
                alpha,
                beta,
                ply,
                out int ttScore,
                out ttPackedMove,
                out hasTtMove,
                out int probedStaticEval,
                out bool hasStaticEval,
                out bool hasCutoff))
        {
            if (hasCutoff)
            {
                return ttScore;
            }
            if (hasStaticEval)
            {
                cachedStaticEval = probedStaticEval;
            }
        }

        // Quiescence search at leaf nodes
        if (depth == 0)
        {
            if (UseQuiescence)
            {
                return Quiescence(in pos, alpha, beta, ply, sw, hardLimitMs, cancellationToken, legalMovesVerified: true, cachedStaticEval, out aborted);
            }
            if (cachedStaticEval != TranspositionEntry.NoStaticEval)
            {
                return cachedStaticEval;
            }
            LeafEvaluations++;
            return EvaluatePosition(in pos);
        }

        Span<BitMove> moveBuffer = ply < MaxPly ? _moveBuffers[ply] : stackalloc BitMove[MaxMovesPerNode];
        int moveCount = BitboardMoveGenerator.Generate(in pos, _variant, moveBuffer);
        if (moveCount == 0)
        {
            return LossScore + ply;
        }

        Span<BitMove> moves = moveBuffer.Slice(0, moveCount);
        bool isCaptureNode = moves[0].IsCapture;
        bool isNullWindow = beta <= alpha + 1;
        bool futilityPrune = false;

        // Stage B Selective Pruning (RFP & FP setup) at non-PV quiet nodes
        if (UseSelectivePruning && !isCaptureNode && isNullWindow && depth <= RfpMaxDepth)
        {
            if (cachedStaticEval == TranspositionEntry.NoStaticEval)
            {
                LeafEvaluations++;
                cachedStaticEval = EvaluatePosition(in pos);
            }

            // 4.2 Reverse Futility Pruning (RFP): if static eval is far above beta at a quiet non-PV node, return immediately
            if (beta < TranspositionTable.WinThreshold &&
                beta > -TranspositionTable.WinThreshold &&
                cachedStaticEval - (RfpMargin * depth) >= beta)
            {
                BitPosition oppPos = pos;
                oppPos.SideToMove = pos.SideToMove == PieceColor.White ? PieceColor.Black : PieceColor.White;
                if (!BitboardMoveGenerator.HasAnyCapture(in oppPos, _variant))
                {
                    return cachedStaticEval;
                }
            }

            // 4.3 Futility Pruning (FP) flag for shallow non-PV quiet nodes far below alpha
            if (depth <= FutilityMaxDepth &&
                alpha > -TranspositionTable.WinThreshold &&
                alpha < TranspositionTable.WinThreshold &&
                cachedStaticEval + (FutilityMargin * depth) <= alpha)
            {
                futilityPrune = true;
            }
        }

        OrderBitMoves(moves, ttPackedMove, hasTtMove, ply, pos.SideToMove);
        _drawTable.Record(ply, pos.Hash);

        BitMove bestMoveThisNode = default;
        bool hasBestMove = false;
        int maxScore = int.MinValue;

        for (int i = 0; i < moves.Length; i++)
        {
            BitPosition nextPos = pos.Apply(in moves[i]);
            bool isQuietMove = !isCaptureNode && !moves[i].IsPromotion;
            bool oppHasCaptureAfterMove = false;
            bool oppCaptureChecked = false;

            // 4.3 Futility Pruning (FP): skip quiet non-promoting moves after move 0 that do not leave a tactical capture
            if (futilityPrune && i > 0 && isQuietMove)
            {
                oppHasCaptureAfterMove = BitboardMoveGenerator.HasAnyCapture(in nextPos, _variant);
                oppCaptureChecked = true;
                if (!oppHasCaptureAfterMove)
                {
                    continue;
                }
            }

            TranspositionTable?.Prefetch(nextPos.Hash);

            int score;
            if (i == 0)
            {
                score = -NegaMax(
                    in nextPos,
                    depth - 1,
                    -beta,
                    -alpha,
                    ply + 1,
                    sw,
                    hardLimitMs,
                    cancellationToken,
                    progress,
                    ref lastReportMs,
                    currentIterDepth,
                    currentRootMove,
                    bestRootMoveOverall,
                    bestScoreOverall,
                    out aborted);
            }
            else
            {
                // 4.1 Verified Late Move Reductions (LMR)
                int reduction = 0;
                if (UseSelectivePruning && isQuietMove && depth >= LmrMinDepth && ply >= LmrMinPly && i >= LmrMinMoveIndex)
                {
                    if (!oppCaptureChecked)
                    {
                        oppHasCaptureAfterMove = BitboardMoveGenerator.HasAnyCapture(in nextPos, _variant);
                    }
                    if (!oppHasCaptureAfterMove)
                    {
                        reduction = 1 + (i >= LmrLateMoveIndex ? 1 : 0);
                        if (reduction > depth - 2)
                        {
                            reduction = depth - 2;
                        }
                    }
                }

                // Principal Variation Search (PVS): test subsequent moves with a zero-width window first (at reduced depth if LMR applies)
                score = -NegaMax(
                    in nextPos,
                    depth - 1 - reduction,
                    -alpha - 1,
                    -alpha,
                    ply + 1,
                    sw,
                    hardLimitMs,
                    cancellationToken,
                    progress,
                    ref lastReportMs,
                    currentIterDepth,
                    currentRootMove,
                    bestRootMoveOverall,
                    bestScoreOverall,
                    out aborted);

                // Verified LMR re-search: if reduced search beat alpha, verify at full depth (depth - 1) on the null window
                if (!aborted && reduction > 0 && score > alpha)
                {
                    score = -NegaMax(
                        in nextPos,
                        depth - 1,
                        -alpha - 1,
                        -alpha,
                        ply + 1,
                        sw,
                        hardLimitMs,
                        cancellationToken,
                        progress,
                        ref lastReportMs,
                        currentIterDepth,
                        currentRootMove,
                        bestRootMoveOverall,
                        bestScoreOverall,
                        out aborted);
                }

                // PVS full-window re-search: if null-window search beat alpha inside a PV node, re-search with full window (-beta, -alpha)
                if (!aborted && !isNullWindow && score > alpha && score < beta)
                {
                    score = -NegaMax(
                        in nextPos,
                        depth - 1,
                        -beta,
                        -alpha,
                        ply + 1,
                        sw,
                        hardLimitMs,
                        cancellationToken,
                        progress,
                        ref lastReportMs,
                        currentIterDepth,
                        currentRootMove,
                        bestRootMoveOverall,
                        bestScoreOverall,
                        out aborted);
                }
            }

            if (aborted)
                return 0;

            if (score > maxScore)
            {
                maxScore = score;
                bestMoveThisNode = moves[i];
                hasBestMove = true;
            }

            if (score > alpha)
            {
                alpha = score;
            }

            if (alpha >= beta)
            {
                if (!moves[i].IsCapture && !moves[i].IsPromotion)
                {
                    RecordKillerAndHistory(in moves[i], ply, depth, pos.SideToMove);
                }
                break;
            }
        }

        // Transposition Table Store
        if (TranspositionTable != null && !aborted)
        {
            TranspositionBound bound;
            if (maxScore <= originalAlpha)
                bound = TranspositionBound.UpperBound;
            else if (maxScore >= beta)
                bound = TranspositionBound.LowerBound;
            else
                bound = TranspositionBound.Exact;

            TranspositionTable.Store(
                pos.Hash,
                depth,
                maxScore,
                bound,
                ply,
                hasBestMove ? bestMoveThisNode.PackedMove : (ushort)0,
                hasBestMove,
                cachedStaticEval);
        }

        return maxScore;
    }

    private int Quiescence(
        in BitPosition pos,
        int alpha,
        int beta,
        int ply,
        Stopwatch sw,
        long hardLimitMs,
        CancellationToken cancellationToken,
        bool legalMovesVerified,
        int cachedStaticEval,
        out bool aborted)
    {
        aborted = false;

        if ((NodesEvaluated & 4095) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sw.ElapsedMilliseconds >= hardLimitMs)
            {
                aborted = true;
                return 0;
            }
        }

        NodesEvaluated++;

        if (!legalMovesVerified)
        {
            ulong ownPieces = pos.SideToMove == PieceColor.White ? pos.White : pos.Black;
            if (ownPieces == 0UL || !BitboardMoveGenerator.HasAnyLegalMove(in pos, _variant))
            {
                return LossScore + ply;
            }
            if (pos.HalfMoveClock >= 80)
            {
                return 0;
            }
        }

        int standPat;
        if (cachedStaticEval != TranspositionEntry.NoStaticEval)
        {
            standPat = cachedStaticEval;
        }
        else
        {
            standPat = EvaluatePosition(in pos);
            LeafEvaluations++;
        }

        if (standPat >= beta)
        {
            return beta;
        }

        if (standPat > alpha)
        {
            alpha = standPat;
        }

        if (ply >= 24 || !BitboardMoveGenerator.HasAnyCapture(in pos, _variant))
        {
            return alpha;
        }

        Span<BitMove> moveBuffer = ply < MaxPly ? _moveBuffers[ply] : stackalloc BitMove[MaxMovesPerNode];
        int captureCount = BitboardMoveGenerator.GenerateCaptures(in pos, _variant, moveBuffer);
        if (captureCount == 0)
        {
            return alpha;
        }

        Span<BitMove> captures = moveBuffer.Slice(0, captureCount);
        OrderCaptureMoves(captures);

        for (int i = 0; i < captures.Length; i++)
        {
            BitPosition nextPos = pos.Apply(in captures[i]);
            int score = -Quiescence(in nextPos, -beta, -alpha, ply + 1, sw, hardLimitMs, cancellationToken, legalMovesVerified: false, TranspositionEntry.NoStaticEval, out aborted);

            if (aborted)
                return 0;

            if (score >= beta)
            {
                return beta;
            }

            if (score > alpha)
            {
                alpha = score;
            }
        }

        return alpha;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordKillerAndHistory(in BitMove move, int ply, int depth, PieceColor sideToMove)
    {
        if ((uint)ply < MaxPly)
        {
            ushort packed = move.PackedMove;
            int killerIdx = ply << 1;
            if (_killers[killerIdx] != packed)
            {
                _killers[killerIdx + 1] = _killers[killerIdx];
                _killers[killerIdx] = packed;
            }
        }

        int histIdx = ((int)sideToMove << 12) | (move.From << 6) | move.To;
        int updated = _history[histIdx] + (depth * depth);
        _history[histIdx] = updated > 70_000 ? 70_000 : updated;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OrderBitMoves(
        Span<BitMove> moves,
        ushort ttPackedMove,
        bool hasTtMove,
        int ply,
        PieceColor sideToMove)
    {
        int count = moves.Length;
        if (count <= 1)
            return;

        Span<int> scores = stackalloc int[count];
        ushort killer1 = 0;
        ushort killer2 = 0;
        if ((uint)ply < MaxPly)
        {
            int killerIdx = ply << 1;
            killer1 = _killers[killerIdx];
            killer2 = _killers[killerIdx + 1];
        }
        int sideOffset = (int)sideToMove << 12;

        for (int i = 0; i < count; i++)
        {
            ref readonly BitMove m = ref moves[i];
            ushort packed = m.PackedMove;
            int score;
            if (hasTtMove && packed == ttPackedMove)
            {
                score = 10_000_000;
            }
            else if (m.IsCapture)
            {
                score = 1_000_000 + (m.CaptureCount * 10_000) + (m.IsPromotion ? 1_000 : 0);
            }
            else if (m.IsPromotion)
            {
                score = 500_000;
            }
            else if (packed == killer1)
            {
                score = 90_000;
            }
            else if (packed == killer2)
            {
                score = 80_000;
            }
            else
            {
                score = _history[sideOffset | (m.From << 6) | m.To];
            }
            scores[i] = score;
        }

        for (int i = 1; i < count; i++)
        {
            int currentScore = scores[i];
            if (currentScore == 0)
                continue;

            BitMove currentMove = moves[i];
            int j = i - 1;
            while (j >= 0 && scores[j] < currentScore)
            {
                moves[j + 1] = moves[j];
                scores[j + 1] = scores[j];
                j--;
            }
            moves[j + 1] = currentMove;
            scores[j + 1] = currentScore;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OrderCaptureMoves(Span<BitMove> captures)
    {
        for (int i = 1; i < captures.Length; i++)
        {
            BitMove current = captures[i];
            int currentKey = (current.CaptureCount << 1) | (current.IsPromotion ? 1 : 0);
            int j = i - 1;
            while (j >= 0)
            {
                int prevKey = (captures[j].CaptureCount << 1) | (captures[j].IsPromotion ? 1 : 0);
                if (prevKey >= currentKey)
                    break;
                captures[j + 1] = captures[j];
                j--;
            }
            captures[j + 1] = current;
        }
    }

    private static string FormatValue(int score) => score switch
    {
        >= TranspositionTable.WinThreshold => $"+Win in {WinScore - score} plies",
        <= -TranspositionTable.WinThreshold => $"-Loss in {score - LossScore} plies",
        > 0 => $"+{score}",
        _ => score.ToString()
    };
}
