using Checkers.Core.AI;
using Checkers.Core.Bitboards;
using Checkers.Core.Models;

namespace Checkers.Core.Engine;

/// <summary>
/// Factory for creating rule engines, evaluation functions, and AI players
/// backed by either the 64-bit <see cref="BoardEngine.Bitboard"/> engine (default)
/// or the classic <see cref="BoardEngine.Array"/> engine.
/// </summary>
public static class EngineFactory
{
    public static IRuleEngine CreateRuleEngine(
        CheckersVariant variant = CheckersVariant.International,
        BoardEngine engine = BoardEngine.Bitboard) =>
        engine == BoardEngine.Array
            ? new RuleEngine(variant)
            : new BitboardRuleEngine(variant);

    public static IEvaluationFunction CreateEvaluationFunction(
        CheckersVariant variant = CheckersVariant.International,
        BoardEngine engine = BoardEngine.Bitboard) =>
        engine == BoardEngine.Array
            ? new EvaluationFunction(variant)
            : new BitboardEvaluation(variant);

    public static IPlayer CreatePlayer(
        SearchLimits limits,
        CheckersVariant variant = CheckersVariant.International,
        BoardEngine engine = BoardEngine.Bitboard,
        TranspositionTable? transpositionTable = null,
        bool useTranspositionTable = true,
        bool useQuiescence = true) =>
        engine == BoardEngine.Array
            ? new MinimaxPlayer(
                limits: limits,
                ruleEngine: new RuleEngine(variant),
                evaluator: new EvaluationFunction(variant),
                transpositionTable: transpositionTable,
                useTranspositionTable: useTranspositionTable,
                useQuiescence: useQuiescence)
            : new BitboardMinimaxPlayer(
                limits: limits,
                variant: variant,
                transpositionTable: transpositionTable,
                useTranspositionTable: useTranspositionTable,
                useQuiescence: useQuiescence);
}
