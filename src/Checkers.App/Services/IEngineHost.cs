using Checkers.App.Models;
using Checkers.Core.AI;
using Checkers.Core.Models;

namespace Checkers.App.Services;

/// <summary>
/// Abstraction for running the Checkers AI engine (in-process on desktop/tests, or in a Web Worker on the browser).
/// </summary>
public interface IEngineHost
{
    Task<(Move Move, SearchAnalysis? LastAnalysis)> ChooseMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        GameSettings settings,
        TimeSpan computerTimeLeft,
        IReadOnlyList<ulong> stateHashHistory,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken,
        CancellationToken moveNowToken);

    void ClearTranspositionTable();
}

