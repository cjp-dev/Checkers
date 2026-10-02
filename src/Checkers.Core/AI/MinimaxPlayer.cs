using System.Diagnostics;
using System.Globalization;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Minimax AI with Alpha-Beta pruning, iterative deepening, PV and TT move ordering,
/// 64-bit Zobrist transposition table, quiescence search, live progress reporting, and time controls.
/// </summary>
public sealed class MinimaxPlayer : IPlayer
{
    private const int WinScore = 100_000;
    private const int LossScore = -100_000;

    private readonly IRuleEngine _ruleEngine;
    private readonly IEvaluationFunction _evaluator;

    public string Name { get; }
    public SearchLimits Limits { get; }
    public int Depth => Limits.Depth;
    public long NodesEvaluated { get; private set; }
    public long LeafEvaluations { get; private set; }
    public SearchAnalysis? LastAnalysis { get; private set; }

    public TranspositionTable? TranspositionTable { get; }
    public bool UseTranspositionTable => TranspositionTable != null;
    public bool UseQuiescence { get; set; } = true;

    public MinimaxPlayer(
        SearchLimits limits,
        string? name = null,
        IRuleEngine? ruleEngine = null,
        IEvaluationFunction? evaluator = null,
        TranspositionTable? transpositionTable = null,
        bool useTranspositionTable = true,
        bool useQuiescence = true)
    {
        Limits = limits;
        Name = name ?? limits.Mode switch
        {
            TimeControlMode.FixedDepth => $"Minimax (Depth {limits.Depth})",
            TimeControlMode.TimePerMove => $"Minimax ({limits.Time.TotalSeconds:0}s/move)",
            TimeControlMode.TimePerGame => "Minimax (Clock)",
            _ => "Minimax AI"
        };
        _ruleEngine = ruleEngine ?? new RuleEngine();
        _evaluator = evaluator ?? new EvaluationFunction();
        TranspositionTable = useTranspositionTable
            ? (transpositionTable ?? new TranspositionTable(megabytes: 32))
            : null;
        UseQuiescence = useQuiescence;
    }

    public MinimaxPlayer(
        int depth = 4,
        string? name = null,
        IRuleEngine? ruleEngine = null,
        IEvaluationFunction? evaluator = null,
        TranspositionTable? transpositionTable = null,
        bool useTranspositionTable = true,
        bool useQuiescence = true)
        : this(SearchLimits.FixedDepth(depth), name, ruleEngine, evaluator, transpositionTable, useTranspositionTable, useQuiescence)
    {
    }

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, progress: null, cancellationToken);

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (legalMoves.Count == 0)
            throw new InvalidOperationException("No legal moves available.");

        // If only 1 move is legal, play it immediately
        if (legalMoves.Count == 1)
        {
            var singleMove = legalMoves[0];
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
            return ValueTask.FromResult(singleMove);
        }

        var sw = Stopwatch.StartNew();
        NodesEvaluated = 0;
        LeafEvaluations = 0;
        long lastReportMs = 0;

        TranspositionTable?.NewSearch();

        // Compute time budgets (soft and hard limits in milliseconds)
        long softLimitMs = long.MaxValue;
        long hardLimitMs = long.MaxValue;
        int maxTargetDepth = Limits.Mode == TimeControlMode.FixedDepth ? Limits.Depth : 20;

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

        Move? bestMoveOverall = null;
        int bestScoreOverall = 0;
        int completedDepth = 1;

        var currentOrder = OrderMoves(legalMoves, null).ToList();

        // Iterative Deepening from depth 1 up to maxTargetDepth
        for (int d = 1; d <= maxTargetDepth; d++)
        {
            // If soft limit reached after finishing previous depth, stop iterating
            if (d > 1 && sw.ElapsedMilliseconds >= softLimitMs)
                break;

            Move? bestMoveThisDepth = null;
            int bestScoreThisDepth = int.MinValue;
            int alpha = -200_000;
            int beta = 200_000;
            bool depthAborted = false;

            foreach (var move in currentOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sw.ElapsedMilliseconds >= hardLimitMs)
                {
                    depthAborted = true;
                    break;
                }

                var nextState = _ruleEngine.ApplyMove(state, move);
                int score = -NegaMax(
                    nextState,
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
                    move,
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
                    bestScoreThisDepth = score;
                    bestMoveThisDepth = move;
                }

                alpha = Math.Max(alpha, score);
            }

            if (!depthAborted && bestMoveThisDepth != null)
            {
                bestMoveOverall = bestMoveThisDepth;
                bestScoreOverall = bestScoreThisDepth;
                completedDepth = d;

                // PV move ordering: prioritize the best move found in this depth
                currentOrder.Remove(bestMoveThisDepth);
                currentOrder.Insert(0, bestMoveThisDepth);

                // Report completed depth iteration live to progress listener
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
                progress?.Report(iterationAnalysis);

                // Stop early if forced win found
                if (bestScoreOverall >= 90_000)
                    break;
            }
            else
            {
                break;
            }
        }

        sw.Stop();

        bestMoveOverall ??= currentOrder[0];

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

        return ValueTask.FromResult(bestMoveOverall);
    }

    private int NegaMax(
        BoardState state,
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

        if ((NodesEvaluated & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sw.ElapsedMilliseconds >= hardLimitMs)
            {
                aborted = true;
                return 0;
            }

            // Periodically report search heartbeat during long depth evaluations
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

        var (status, _) = _ruleEngine.EvaluateGameStatus(state);
        if (status != GameStatus.InProgress)
        {
            if (status == GameStatus.Draw)
                return 0;

            // Current player lost/blocked/eliminated
            return LossScore + ply;
        }

        int originalAlpha = alpha;
        Move? ttMove = null;
        Position ttFrom = default;
        Position ttTo = default;

        // Transposition Table Probe
        if (TranspositionTable != null &&
            TranspositionTable.TryProbe(state.ZobristHash, depth, alpha, beta, ply, out int ttScore, out ttFrom, out ttTo, out bool hasCutoff))
        {
            if (hasCutoff)
            {
                return ttScore;
            }
        }

        // Quiescence search at leaf nodes
        if (depth == 0)
        {
            if (UseQuiescence)
            {
                return Quiescence(state, alpha, beta, ply, sw, hardLimitMs, cancellationToken, out aborted);
            }
            LeafEvaluations++;
            return _evaluator.Evaluate(state);
        }

        var legalMoves = _ruleEngine.GetLegalMoves(state);
        if (legalMoves.Count == 0)
        {
            return LossScore + ply;
        }

        if (TranspositionTable != null && ttFrom.IsValid && ttTo.IsValid)
        {
            ttMove = legalMoves.FirstOrDefault(m => m.From == ttFrom && m.To == ttTo);
        }

        var orderedMoves = OrderMoves(legalMoves, ttMove);
        Move? bestMoveThisNode = null;
        int maxScore = int.MinValue;

        foreach (var move in orderedMoves)
        {
            var nextState = _ruleEngine.ApplyMove(state, move);
            int score = -NegaMax(
                nextState,
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

            if (aborted)
                return 0;

            if (score > maxScore)
            {
                maxScore = score;
                bestMoveThisNode = move;
            }

            alpha = Math.Max(alpha, score);

            if (alpha >= beta)
            {
                // Beta cutoff
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
                state.ZobristHash,
                depth,
                maxScore,
                bound,
                ply,
                bestMoveThisNode?.From ?? default,
                bestMoveThisNode?.To ?? default);
        }

        return maxScore;
    }

    private int Quiescence(
        BoardState state,
        int alpha,
        int beta,
        int ply,
        Stopwatch sw,
        long hardLimitMs,
        CancellationToken cancellationToken,
        out bool aborted)
    {
        aborted = false;

        if ((NodesEvaluated & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sw.ElapsedMilliseconds >= hardLimitMs)
            {
                aborted = true;
                return 0;
            }
        }

        NodesEvaluated++;

        var (status, _) = _ruleEngine.EvaluateGameStatus(state);
        if (status != GameStatus.InProgress)
        {
            return status == GameStatus.Draw ? 0 : LossScore + ply;
        }

        int standPat = _evaluator.Evaluate(state);
        LeafEvaluations++;

        if (standPat >= beta)
        {
            return beta;
        }

        if (standPat > alpha)
        {
            alpha = standPat;
        }

        // Limit maximum quiescence search extension to prevent runaway search
        if (ply >= 24)
        {
            return alpha;
        }

        var legalMoves = _ruleEngine.GetLegalMoves(state);
        var captureMoves = legalMoves.Where(m => m.IsCapture).ToList();

        // In Checkers, if there are captures, mandatory jumping applies.
        // If there are no captures, the position is quiet.
        if (captureMoves.Count == 0)
        {
            return alpha;
        }

        var orderedCaptures = OrderMoves(captureMoves, null);

        foreach (var move in orderedCaptures)
        {
            var nextState = _ruleEngine.ApplyMove(state, move);
            int score = -Quiescence(nextState, -beta, -alpha, ply + 1, sw, hardLimitMs, cancellationToken, out aborted);

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

    private static IReadOnlyList<Move> OrderMoves(IEnumerable<Move> moves, Move? ttMove = null)
    {
        var list = moves
            .OrderByDescending(m => m.CapturedPositions.Count) // Multi-jumps first
            .ThenByDescending(m => m.IsPromotion ? 1 : 0)     // Promotions next
            .ToList();

        if (ttMove != null)
        {
            int index = list.FindIndex(m => m.From == ttMove.From && m.To == ttMove.To);
            if (index > 0)
            {
                var move = list[index];
                list.RemoveAt(index);
                list.Insert(0, move);
            }
        }

        return list;
    }

    private static string FormatValue(int score) => score switch
    {
        >= 90_000 => $"+Win in {WinScore - score} plies",
        <= -90_000 => $"-Loss in {score - LossScore} plies",
        > 0 => $"+{score}",
        _ => score.ToString()
    };
}
