using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI.Benchmark;

/// <summary>
/// Provides a deterministic suite of 40 diverse legal Checkers positions
/// algorithmically generated across openings, middlegames, endgames, and blockades.
/// </summary>
public static class BenchmarkSuite
{
    public static IReadOnlyList<BenchmarkPosition> GetPositions()
    {
        var ruleEngine = new RuleEngine();
        var positions = new List<BenchmarkPosition>(40);

        // 1. Generate 10 Opening Positions (Moves 4 to 12)
        int[] openingSeeds = [101, 203, 307, 409, 521, 631, 743, 857, 967, 1087];
        for (int i = 0; i < openingSeeds.Length; i++)
        {
            var state = SimulateToCondition(openingSeeds[i], ruleEngine,
                stopCondition: (s, moveCount) => moveCount >= 4 + (i % 6) && s.WhitePiecesCount >= 10);
            positions.Add(new BenchmarkPosition(
                Id: positions.Count + 1,
                Category: "Opening",
                Description: $"Opening phase (Move {state.FullMoveNumber}, {state.WhitePiecesCount + state.BlackPiecesCount} pieces)",
                State: state));
        }

        // 2. Generate 15 Tactical Middlegame Positions (Moves 14 to 32, multi-jumps, king promotions)
        int[] middlegameSeeds = [1129, 1249, 1373, 1499, 1619, 1753, 1877, 2003, 2131, 2269, 2399, 2539, 2677, 2803, 2953];
        for (int i = 0; i < middlegameSeeds.Length; i++)
        {
            var state = SimulateToCondition(middlegameSeeds[i], ruleEngine,
                stopCondition: (s, moveCount) => moveCount >= 14 + (i % 12) && (s.WhiteKingsCount + s.BlackKingsCount >= 1 || (s.WhitePiecesCount + s.BlackPiecesCount) <= 18));
            positions.Add(new BenchmarkPosition(
                Id: positions.Count + 1,
                Category: "Middlegame",
                Description: $"Tactical middlegame (Move {state.FullMoveNumber}, {state.WhitePiecesCount + state.BlackPiecesCount} pieces, {state.WhiteKingsCount + state.BlackKingsCount} kings)",
                State: state));
        }

        // 3. Generate 10 Endgame Positions (<= 8 pieces, flying king dynamics)
        int[] endgameSeeds = [3109, 3253, 3407, 3557, 3709, 3877, 4021, 4177, 4337, 4507];
        for (int i = 0; i < endgameSeeds.Length; i++)
        {
            var state = SimulateToCondition(endgameSeeds[i], ruleEngine,
                stopCondition: (s, moveCount) => moveCount >= 28 && (s.WhitePiecesCount + s.BlackPiecesCount) <= 8);
            positions.Add(new BenchmarkPosition(
                Id: positions.Count + 1,
                Category: "Endgame",
                Description: $"Endgame balance (Move {state.FullMoveNumber}, {state.WhitePiecesCount + state.BlackPiecesCount} pieces, {state.WhiteKingsCount + state.BlackKingsCount} kings)",
                State: state));
        }

        // 4. Generate 5 Blockade & Tension Positions
        int[] blockadeSeeds = [4673, 4831, 5003, 5171, 5347];
        for (int i = 0; i < blockadeSeeds.Length; i++)
        {
            var state = SimulateToCondition(blockadeSeeds[i], ruleEngine,
                stopCondition: (s, moveCount) => moveCount >= 20 && ruleEngine.GetLegalMoves(s).Count <= 4);
            positions.Add(new BenchmarkPosition(
                Id: positions.Count + 1,
                Category: "Blockade/Tension",
                Description: $"Restricted mobility tension (Move {state.FullMoveNumber}, {ruleEngine.GetLegalMoves(state).Count} legal moves)",
                State: state));
        }

        return positions;
    }

    private static BoardState SimulateToCondition(
        int seed,
        RuleEngine ruleEngine,
        Func<BoardState, int, bool> stopCondition)
    {
        var rng = new Random(seed);
        var state = BoardState.CreateInitial();
        int moveCount = 0;

        while (moveCount < 80)
        {
            var (status, _) = ruleEngine.EvaluateGameStatus(state);
            if (status != GameStatus.InProgress)
                break;

            var legalMoves = ruleEngine.GetLegalMoves(state);
            if (legalMoves.Count == 0)
                break;

            if (moveCount > 0 && stopCondition(state, moveCount))
            {
                return state;
            }

            // Pick a move with slight bias toward captures
            var captureMoves = legalMoves.Where(m => m.IsCapture).ToList();
            Move selectedMove;
            if (captureMoves.Count > 0)
            {
                selectedMove = captureMoves[rng.Next(captureMoves.Count)];
            }
            else
            {
                selectedMove = legalMoves[rng.Next(legalMoves.Count)];
            }

            state = ruleEngine.ApplyMove(state, selectedMove);
            moveCount++;
        }

        // If loop finished without trigger, return state if legal moves exist, else return initial
        if (ruleEngine.GetLegalMoves(state).Count > 0 && ruleEngine.EvaluateGameStatus(state).Status == GameStatus.InProgress)
            return state;

        return BoardState.CreateInitial();
    }
}
