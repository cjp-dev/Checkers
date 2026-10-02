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
    public long LeafEvaluations { get; private set; }
    public SearchAnalysis? LastAnalysis { get; private set; }

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
            var singleMove = legalMoves[0];
            LastAnalysis = new SearchAnalysis
            {
                Move = singleMove.Notation,
                Depth = $"{Depth} plies (Forced)",
                Value = "Forced",
                BestMove = singleMove.Notation,
                Nodes = "1",
                Evaluations = "0",
                Time = "0:00.0"
            };
            return ValueTask.FromResult(singleMove);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        NodesEvaluated = 0;
        LeafEvaluations = 0;

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

        sw.Stop();

        // Among moves tied for the best score, choose uniformly at random
        int selectedIndex = _rng.Next(bestMoves.Count);
        var chosenMove = bestMoves[selectedIndex];

        string valueStr = bestScore switch
        {
            >= 90_000 => $"+Win in {WinScore - bestScore} plies",
            <= -90_000 => $"-Loss in {bestScore - LossScore} plies",
            > 0 => $"+{bestScore}",
            _ => bestScore.ToString()
        };

        LastAnalysis = new SearchAnalysis
        {
            Move = chosenMove.Notation,
            Depth = $"{Depth} plies",
            Value = valueStr,
            BestMove = chosenMove.Notation,
            Nodes = NodesEvaluated.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            Evaluations = LeafEvaluations.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            Time = sw.Elapsed.ToString(@"m\:ss\.f")
        };

        return ValueTask.FromResult(chosenMove);
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
