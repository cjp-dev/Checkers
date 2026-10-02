using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Minimax AI with Alpha-Beta pruning, move ordering, and heuristic evaluation.
/// </summary>
public sealed class MinimaxPlayer : IPlayer
{
    private const int WinScore = 100_000;
    private const int LossScore = -100_000;

    private readonly IRuleEngine _ruleEngine;
    private readonly IEvaluationFunction _evaluator;
    private readonly Random _rng = new(42);

    public string Name { get; }
    public int Depth { get; }
    public long NodesEvaluated { get; private set; }

    public MinimaxPlayer(
        int depth = 4,
        string? name = null,
        IRuleEngine? ruleEngine = null,
        IEvaluationFunction? evaluator = null)
    {
        if (depth < 1)
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be at least 1.");

        Depth = depth;
        Name = name ?? $"Minimax (Depth {depth})";
        _ruleEngine = ruleEngine ?? new RuleEngine();
        _evaluator = evaluator ?? new EvaluationFunction();
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
            return ValueTask.FromResult(legalMoves[0]);
        }

        NodesEvaluated = 0;

        var orderedMoves = OrderMoves(legalMoves);
        int alpha = -200_000;
        int beta = 200_000;

        var bestMoves = new List<Move>();
        int bestScore = int.MinValue;

        foreach (var move in orderedMoves)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextState = _ruleEngine.ApplyMove(state, move);
            int score = -NegaMax(nextState, Depth - 1, -beta, -alpha, ply: 1, cancellationToken);

            if (score > bestScore)
            {
                bestScore = score;
                bestMoves.Clear();
                bestMoves.Add(move);
            }
            else if (score == bestScore)
            {
                bestMoves.Add(move);
            }

            alpha = Math.Max(alpha, score);
        }

        // Among moves tied for the best score, choose uniformly at random
        int selectedIndex = _rng.Next(bestMoves.Count);
        return ValueTask.FromResult(bestMoves[selectedIndex]);
    }

    private int NegaMax(
        BoardState state,
        int depth,
        int alpha,
        int beta,
        int ply,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            int score = -NegaMax(nextState, depth - 1, -beta, -alpha, ply + 1, cancellationToken);

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
