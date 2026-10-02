using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Common abstraction for a checkers player (Human, Random, Minimax AI, etc.).
/// </summary>
public interface IPlayer
{
    string Name { get; }

    /// <summary>
    /// Chooses a move from the available legal moves for the given state.
    /// </summary>
    ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        CancellationToken cancellationToken = default);
}
