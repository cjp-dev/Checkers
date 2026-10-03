using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI.Benchmark;

/// <summary>
/// Provides a deterministic suite of 40 diverse legal Checkers positions
/// algorithmically generated across openings, middlegames, endgames, and blockades.
/// Every position is guaranteed to be unique and have at least 2 legal moves with non-trivial branching.
/// </summary>
public static class BenchmarkSuite
{
    public static IReadOnlyList<BenchmarkPosition> GetPositions()
    {
        var ruleEngine = new RuleEngine();
        var positions = new List<BenchmarkPosition>(40);
        var seenHashes = new HashSet<ulong>();

        // 1. Generate 10 Opening Positions (Moves 4 to 12, high piece count, >= 4 legal moves)
        int[] openingSeeds = [101, 203, 307, 409, 521, 631, 743, 857, 967, 1087];
        for (int i = 0; i < openingSeeds.Length; i++)
        {
            int targetMinMove = 4 + (i % 6);
            var state = FindPositionMatching(
                openingSeeds[i],
                ruleEngine,
                seenHashes,
                stopCondition: (s, moveCount, legal) =>
                    moveCount >= targetMinMove &&
                    moveCount <= 14 &&
                    s.WhitePiecesCount >= 9 &&
                    s.BlackPiecesCount >= 9 &&
                    legal.Count >= 4);

            positions.Add(new BenchmarkPosition(
                Id: positions.Count + 1,
                Category: "Opening",
                Description: $"Opening phase (Move {state.FullMoveNumber}, {state.WhitePiecesCount + state.BlackPiecesCount} pieces, {ruleEngine.GetLegalMoves(state).Count} legal moves)",
                State: state));
        }

        // 2. Generate 15 Tactical Middlegame Positions (Moves 14 to 36, multi-jumps, king promotions)
        int[] middlegameSeeds = [1129, 1249, 1373, 1499, 1619, 1753, 1877, 2003, 2131, 2269, 2399, 2539, 2677, 2803, 2953];
        for (int i = 0; i < middlegameSeeds.Length; i++)
        {
            int targetMinMove = 14 + (i % 12);
            var state = FindPositionMatching(
                middlegameSeeds[i],
                ruleEngine,
                seenHashes,
                stopCondition: (s, moveCount, legal) =>
                {
                    int totalPieces = s.WhitePiecesCount + s.BlackPiecesCount;
                    int totalKings = s.WhiteKingsCount + s.BlackKingsCount;
                    return moveCount >= targetMinMove &&
                           totalPieces is >= 10 and <= 20 &&
                           (totalKings >= 1 || totalPieces <= 18) &&
                           legal.Count >= 3;
                });

            positions.Add(new BenchmarkPosition(
                Id: positions.Count + 1,
                Category: "Middlegame",
                Description: $"Tactical middlegame (Move {state.FullMoveNumber}, {state.WhitePiecesCount + state.BlackPiecesCount} pieces, {state.WhiteKingsCount + state.BlackKingsCount} kings)",
                State: state));
        }

        // 3. Generate 10 Endgame Positions (<= 10 pieces, flying king dynamics)
        int[] endgameSeeds = [3109, 3253, 3407, 3557, 3709, 3877, 4021, 4177, 4337, 4507];
        for (int i = 0; i < endgameSeeds.Length; i++)
        {
            var state = FindPositionMatching(
                endgameSeeds[i],
                ruleEngine,
                seenHashes,
                stopCondition: (s, moveCount, legal) =>
                {
                    int totalPieces = s.WhitePiecesCount + s.BlackPiecesCount;
                    int totalKings = s.WhiteKingsCount + s.BlackKingsCount;
                    return moveCount >= 24 &&
                           totalPieces is >= 4 and <= 10 &&
                           s.WhitePiecesCount >= 2 &&
                           s.BlackPiecesCount >= 2 &&
                           totalKings >= 1 &&
                           legal.Count >= 3;
                });

            positions.Add(new BenchmarkPosition(
                Id: positions.Count + 1,
                Category: "Endgame",
                Description: $"Endgame balance (Move {state.FullMoveNumber}, {state.WhitePiecesCount + state.BlackPiecesCount} pieces, {state.WhiteKingsCount + state.BlackKingsCount} kings)",
                State: state));
        }

        // 4. Generate 5 Blockade & Tension Positions (2 to 4 legal moves, non-forced)
        int[] blockadeSeeds = [4673, 4831, 5003, 5171, 5347];
        for (int i = 0; i < blockadeSeeds.Length; i++)
        {
            var state = FindPositionMatching(
                blockadeSeeds[i],
                ruleEngine,
                seenHashes,
                stopCondition: (s, moveCount, legal) =>
                    moveCount >= 16 &&
                    (s.WhitePiecesCount + s.BlackPiecesCount) >= 8 &&
                    legal.Count is >= 2 and <= 4);

            positions.Add(new BenchmarkPosition(
                Id: positions.Count + 1,
                Category: "Blockade/Tension",
                Description: $"Restricted mobility tension (Move {state.FullMoveNumber}, {ruleEngine.GetLegalMoves(state).Count} legal moves)",
                State: state));
        }

        return positions;
    }

    private static BoardState FindPositionMatching(
        int initialSeed,
        RuleEngine ruleEngine,
        HashSet<ulong> seenHashes,
        Func<BoardState, int, IReadOnlyList<Move>, bool> stopCondition)
    {
        int seed = initialSeed;
        var englishEngine = new RuleEngine(CheckersVariant.English);

        for (int attempt = 0; attempt < 200; attempt++)
        {
            var rng = new Random(seed);
            var state = BoardState.CreateInitial();
            int moveCount = 0;

            while (moveCount < 100)
            {
                var (status, _) = ruleEngine.EvaluateGameStatus(state);
                if (status != GameStatus.InProgress)
                    break;

                var legalMoves = ruleEngine.GetLegalMoves(state);
                if (legalMoves.Count == 0)
                    break;

                if (moveCount > 0 &&
                    legalMoves.Count >= 2 &&
                    !seenHashes.Contains(state.ZobristHash) &&
                    stopCondition(state, moveCount, legalMoves) &&
                    HasNonTrivialBranching(state, legalMoves, ruleEngine))
                {
                    var englishMoves = englishEngine.GetLegalMoves(state);
                    if (englishMoves.Count >= 2 &&
                        HasNonTrivialBranching(state, englishMoves, englishEngine))
                    {
                        seenHashes.Add(state.ZobristHash);
                        return state;
                    }
                }

                // Pick a move with slight bias toward captures
                var captureMoves = legalMoves.Where(m => m.IsCapture).ToList();
                Move selectedMove = captureMoves.Count > 0
                    ? captureMoves[rng.Next(captureMoves.Count)]
                    : legalMoves[rng.Next(legalMoves.Count)];

                state = ruleEngine.ApplyMove(state, selectedMove);
                moveCount++;
            }

            seed += 37;
        }

        throw new InvalidOperationException($"Failed to generate benchmark position for seed {initialSeed}.");
    }

    /// <summary>
    /// Verifies that the position has genuine multi-ply search branching and is not an immediate
    /// forced win/loss within 5 plies.
    /// </summary>
    private static bool HasNonTrivialBranching(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        RuleEngine ruleEngine)
    {
        int branchingChildren = 0;
        foreach (var move in legalMoves)
        {
            var next = ruleEngine.ApplyMove(state, move);
            if (ruleEngine.EvaluateGameStatus(next).Status == GameStatus.InProgress &&
                ruleEngine.GetLegalMoves(next).Count >= 2)
            {
                branchingChildren++;
                if (branchingChildren >= 2)
                    break;
            }
        }

        if (branchingChildren < 2)
            return false;

        var probePlayer = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(5),
            variant: ruleEngine.Variant,
            useTranspositionTable: false,
            useQuiescence: true);
        probePlayer.GetMoveAsync(state, legalMoves).AsTask().GetAwaiter().GetResult();

        string val = probePlayer.LastAnalysis?.Value ?? string.Empty;
        return probePlayer.NodesEvaluated >= 300 &&
               !val.Contains("Win", StringComparison.OrdinalIgnoreCase) &&
               !val.Contains("Loss", StringComparison.OrdinalIgnoreCase);
    }
}
