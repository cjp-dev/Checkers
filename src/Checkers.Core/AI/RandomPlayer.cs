using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Baseline AI player that selects uniformly at random from available legal moves.
/// </summary>
public sealed class RandomPlayer : IPlayer
{
    private readonly Random _random;

    public string Name { get; }

    public RandomPlayer(string name = "Random AI", int? seed = null)
    {
        Name = name;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (legalMoves.Count == 0)
            throw new InvalidOperationException("No legal moves available.");

        int index = _random.Next(legalMoves.Count);
        return ValueTask.FromResult(legalMoves[index]);
    }
}
