using Checkers.App.Models;
using Checkers.Core.AI;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.App.Services;

/// <summary>
/// Runs the Checkers AI engine on a thread-pool thread in the current process.
/// </summary>
public sealed class LocalEngineHost : IEngineHost
{
    private TranspositionTable? _transpositionTable;
    private CheckersVariant _lastVariant = GameSettings.Default.Variant;

    public Func<GameSettings, TimeSpan, IPlayer>? PlayerFactory { get; set; }

    public IPlayer CreatePlayer(GameSettings settings, TimeSpan computerTimeLeft)
    {
        if (PlayerFactory != null)
        {
            return PlayerFactory(settings, computerTimeLeft);
        }

        if (_transpositionTable == null || _transpositionTable.Capacity != settings.TranspositionTableEntries)
        {
            _transpositionTable = TranspositionTable.FromEntries(settings.TranspositionTableEntries);
        }
        else if (_lastVariant != settings.Variant)
        {
            _transpositionTable.Clear();
        }
        _lastVariant = settings.Variant;

        return new MinimaxPlayer(
            settings.ToLimits(computerTimeLeft),
            ruleEngine: new RuleEngine(settings.Variant),
            evaluator: new EvaluationFunction(settings.Variant),
            transpositionTable: _transpositionTable);
    }

    public async Task<(Move Move, SearchAnalysis? LastAnalysis)> ChooseMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        GameSettings settings,
        TimeSpan computerTimeLeft,
        IReadOnlyList<ulong> stateHashHistory,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken,
        CancellationToken moveNowToken)
    {
        IPlayer ai = CreatePlayer(settings, computerTimeLeft);

        Move move = await Task.Run(
            async () => await ai.GetMoveAsync(state, legalMoves, progress, stateHashHistory, cancellationToken, moveNowToken),
            cancellationToken);

        SearchAnalysis? lastAnalysis = ai.LastAnalysis;
        return (move, lastAnalysis);
    }

    public void ClearTranspositionTable() => _transpositionTable?.Clear();
}
