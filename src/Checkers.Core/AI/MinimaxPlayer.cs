using System.Diagnostics;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Minimax AI with Alpha-Beta pruning, iterative deepening, PV move ordering, and time controls.
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

    public MinimaxPlayer(
        SearchLimits limits,
        string? name = null,
        IRuleEngine? ruleEngine = null,
        IEvaluationFunction? evaluator = null)
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
    }

    public MinimaxPlayer(
        int depth = 4,
        string? name = null,
        IRuleEngine? ruleEngine = null,
        IEvaluationFunction? evaluator = null)
        : this(SearchLimits.FixedDepth(depth), name, ruleEngine, evaluator)
    {
    }

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
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
            return ValueTask.FromResult(singleMove);
        }

        var sw = Stopwatch.StartNew();
        NodesEvaluated = 0;
        LeafEvaluations = 0;

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

        var currentOrder = OrderMoves(legalMoves).ToList();

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
                int score = -NegaMax(nextState, d - 1, -beta, -alpha, ply: 1, sw, hardLimitMs, cancellationToken, out bool aborted);

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

        string valueStr = bestScoreOverall switch
        {
            >= 90_000 => $"+Win in {WinScore - bestScoreOverall} plies",
            <= -90_000 => $"-Loss in {bestScoreOverall - LossScore} plies",
            > 0 => $"+{bestScoreOverall}",
            _ => bestScoreOverall.ToString()
        };

        LastAnalysis = new SearchAnalysis
        {
            Move = bestMoveOverall.Notation,
            Depth = $"{completedDepth} plies",
            Value = valueStr,
            BestMove = bestMoveOverall.Notation,
            Nodes = NodesEvaluated.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            Evaluations = LeafEvaluations.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            Time = sw.Elapsed.ToString(@"m\:ss\.f")
        };

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
        out bool aborted)
    {
        aborted = false;

        if ((NodesEvaluated & 511) == 0)
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
            if (status == GameStatus.Draw)
                return 0;

            // Current player lost/blocked/eliminated
            return LossScore + ply;
        }

        if (depth == 0)
        {
            LeafEvaluations++;
            return _evaluator.Evaluate(state);
        }

        var legalMoves = _ruleEngine.GetLegalMoves(state);
        if (legalMoves.Count == 0)
        {
            return LossScore + ply;
        }

        var orderedMoves = OrderMoves(legalMoves);
        int maxScore = int.MinValue;

        foreach (var move in orderedMoves)
        {
            var nextState = _ruleEngine.ApplyMove(state, move);
            int score = -NegaMax(nextState, depth - 1, -beta, -alpha, ply + 1, sw, hardLimitMs, cancellationToken, out aborted);

            if (aborted)
                return 0;

            maxScore = Math.Max(maxScore, score);
            alpha = Math.Max(alpha, score);

            if (alpha >= beta)
            {
                // Alpha-Beta cutoff
                break;
            }
        }

        return maxScore;
    }

    private static IReadOnlyList<Move> OrderMoves(IEnumerable<Move> moves) =>
        moves
            .OrderByDescending(m => m.CapturedPositions.Count) // Multi-jumps first
            .ThenByDescending(m => m.IsPromotion ? 1 : 0)     // Promotions next
            .ToList();
}
