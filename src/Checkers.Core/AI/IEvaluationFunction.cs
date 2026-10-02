using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Static heuristic evaluation function for board states.
/// </summary>
public interface IEvaluationFunction
{
    /// <summary>
    /// Evaluates the board state relative to the active player.
    /// Positive values favor the active player; negative values favor the opponent.
    /// </summary>
    int Evaluate(BoardState state);
}
