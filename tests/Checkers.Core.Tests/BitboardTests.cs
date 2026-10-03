using System.Numerics;
using Checkers.Core.AI;
using Checkers.Core.AI.Benchmark;
using Checkers.Core.Bitboards;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class BitboardTests
{
    [Fact]
    public void BitboardMasks_HaveExpectedPopCountsAndCoordinates()
    {
        BitOperations.PopCount(BitboardMasks.DarkSquares).Should().Be(32);
        BitOperations.PopCount(BitboardMasks.CenterMask).Should().Be(4);
        BitOperations.PopCount(BitboardMasks.KingCenterMask).Should().Be(8);

        for (int r = 0; r < 8; r++)
        {
            BitOperations.PopCount(BitboardMasks.Rows[r]).Should().Be(8);
            for (int c = 0; c < 8; c++)
            {
                int sq = BitboardMasks.ToSquareIndex(r, c);
                sq.Should().Be(r * 8 + c);
                BitboardMasks.ToPosition(sq).Should().Be(new Position(r, c));

                bool isDark = ((BitboardMasks.DarkSquares >> sq) & 1UL) != 0;
                isDark.Should().Be(new Position(r, c).IsDarkSquare);
            }
        }
    }

    [Fact]
    public void BitPosition_RoundTripsInitialAndCustomBoardStateWithIdenticalHash()
    {
        var initial = BoardState.CreateInitial();
        var bitPos = BitPosition.FromBoardState(initial);

        bitPos.WhitePiecesCount.Should().Be(12);
        bitPos.BlackPiecesCount.Should().Be(12);
        bitPos.Hash.Should().Be(initial.ZobristHash);

        var roundTrip = bitPos.ToBoardState(initial.FullMoveNumber);
        roundTrip.ZobristHash.Should().Be(initial.ZobristHash);
        roundTrip.WhitePiecesCount.Should().Be(12);
        roundTrip.BlackPiecesCount.Should().Be(12);

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                roundTrip.GetPiece(r, c).Should().Be(initial.GetPiece(r, c));
            }
        }
    }

    [Theory]
    [InlineData(CheckersVariant.International)]
    [InlineData(CheckersVariant.English)]
    public void EngineEquivalence_50RandomGamesPerVariant_MatchMoveForMoveAndHashForHash(CheckersVariant variant)
    {
        var arrayEngine = new RuleEngine(variant);
        var bitboardEngine = new BitboardRuleEngine(variant);
        var arrayEval = new EvaluationFunction(variant);
        var bitboardEval = new BitboardEvaluation(variant);

        Span<BitMove> moveBuffer = stackalloc BitMove[128];

        for (int seed = 1; seed <= 50; seed++)
        {
            var rng = new Random(seed * 997 + (int)variant * 31);
            var state = BoardState.CreateInitial();

            for (int ply = 0; ply < 80; ply++)
            {
                // 1. Static evaluation equivalence
                int evalArr = arrayEval.Evaluate(state);
                int evalBit = bitboardEval.Evaluate(state);
                evalBit.Should().Be(evalArr, $"Evaluation mismatch at seed {seed}, ply {ply}, variant {variant}");

                // 2. Game status equivalence
                var statusArr = arrayEngine.EvaluateGameStatus(state);
                var statusBit = bitboardEngine.EvaluateGameStatus(state);
                statusBit.Should().Be(statusArr, $"Status mismatch at seed {seed}, ply {ply}, variant {variant}");

                if (statusArr.Status != GameStatus.InProgress)
                    break;

                // 3. Full Move list equivalence (order, From, To, Path, CapturedPositions, IsPromotion, Notation)
                var movesArr = arrayEngine.GetLegalMoves(state);
                var movesBit = bitboardEngine.GetLegalMoves(state);

                movesBit.Count.Should().Be(movesArr.Count, $"Move count mismatch at seed {seed}, ply {ply}, variant {variant}");

                var bitPos = BitPosition.FromBoardState(state);
                int rawBitMoveCount = BitboardMoveGenerator.Generate(in bitPos, variant, moveBuffer);
                rawBitMoveCount.Should().Be(movesArr.Count);

                for (int i = 0; i < movesArr.Count; i++)
                {
                    var ma = movesArr[i];
                    var mb = movesBit[i];

                    mb.From.Should().Be(ma.From);
                    mb.To.Should().Be(ma.To);
                    mb.IsPromotion.Should().Be(ma.IsPromotion);
                    mb.Notation.Should().Be(ma.Notation);
                    mb.Path.Should().Equal(ma.Path);
                    mb.CapturedPositions.Should().Equal(ma.CapturedPositions);

                    // Verify raw BitMove matches full Move
                    var rawBm = moveBuffer[i];
                    BitboardMasks.ToPosition(rawBm.From).Should().Be(ma.From);
                    BitboardMasks.ToPosition(rawBm.To).Should().Be(ma.To);
                    rawBm.IsPromotion.Should().Be(ma.IsPromotion);
                    rawBm.CaptureCount.Should().Be(ma.CapturedPositions.Count);
                }

                // 4. Pick move and verify ApplyMove & incremental Zobrist hash equivalence
                int chosenIdx = rng.Next(movesArr.Count);
                var chosenMove = movesArr[chosenIdx];

                var nextArr = arrayEngine.ApplyMove(state, chosenMove);
                var nextBit = bitboardEngine.ApplyMove(state, chosenMove);

                nextBit.ZobristHash.Should().Be(nextArr.ZobristHash, $"ZobristHash mismatch at seed {seed}, ply {ply}, move {chosenMove}");
                nextBit.ActivePlayer.Should().Be(nextArr.ActivePlayer);
                nextBit.HalfMoveClock.Should().Be(nextArr.HalfMoveClock);
                nextBit.FullMoveNumber.Should().Be(nextArr.FullMoveNumber);
                nextBit.WhitePiecesCount.Should().Be(nextArr.WhitePiecesCount);
                nextBit.BlackPiecesCount.Should().Be(nextArr.BlackPiecesCount);
                nextBit.WhiteKingsCount.Should().Be(nextArr.WhiteKingsCount);
                nextBit.BlackKingsCount.Should().Be(nextArr.BlackKingsCount);

                state = nextArr;
            }
        }
    }

    [Theory]
    [InlineData(CheckersVariant.International, 1, 7)]
    [InlineData(CheckersVariant.International, 2, 49)]
    [InlineData(CheckersVariant.International, 3, 302)]
    [InlineData(CheckersVariant.International, 4, 1469)]
    [InlineData(CheckersVariant.International, 5, 7361)]
    [InlineData(CheckersVariant.English, 1, 7)]
    [InlineData(CheckersVariant.English, 2, 49)]
    [InlineData(CheckersVariant.English, 3, 302)]
    [InlineData(CheckersVariant.English, 4, 1469)]
    [InlineData(CheckersVariant.English, 5, 7361)]
    public void Perft_InitialPosition_MatchesExactNodeCountsAndArrayEngine(
        CheckersVariant variant,
        int depth,
        long expectedLeafNodes)
    {
        var initial = BoardState.CreateInitial();
        var arrayEngine = new RuleEngine(variant);
        var bitPos = BitPosition.FromBoardState(initial);

        long arrayNodes = PerftArray(arrayEngine, initial, depth);
        long bitboardNodes = PerftBitboard(in bitPos, variant, depth);

        bitboardNodes.Should().Be(arrayNodes);
        bitboardNodes.Should().Be(expectedLeafNodes);
    }

    [Theory]
    [InlineData(CheckersVariant.International)]
    [InlineData(CheckersVariant.English)]
    public async Task SearchEquivalence_ArrayAndBitboardMinimax_ProduceIdenticalNodesScoreAndMove(CheckersVariant variant)
    {
        var positions = BenchmarkSuite.GetPositions();
        var sampleIndices = new[] { 0, 5, 12, 18, 26, 33, 38 };

        foreach (int idx in sampleIndices)
        {
            var state = positions[idx].State;
            var arrayEngine = new RuleEngine(variant);
            var legalMoves = arrayEngine.GetLegalMoves(state);

            // 1. Without Transposition Table (Depth 5)
            var arrNoTt = new MinimaxPlayer(
                depth: 5,
                ruleEngine: arrayEngine,
                evaluator: new EvaluationFunction(variant),
                useTranspositionTable: false,
                useQuiescence: true);
            var bitNoTt = new BitboardMinimaxPlayer(
                depth: 5,
                variant: variant,
                useTranspositionTable: false,
                useQuiescence: true);

            var moveArrNoTt = await arrNoTt.GetMoveAsync(state, legalMoves);
            var moveBitNoTt = await bitNoTt.GetMoveAsync(state, legalMoves);

            bitNoTt.NodesEvaluated.Should().Be(arrNoTt.NodesEvaluated, $"No-TT node count mismatch on Pos #{idx + 1} ({variant})");
            bitNoTt.LeafEvaluations.Should().Be(arrNoTt.LeafEvaluations, $"No-TT leaf evals mismatch on Pos #{idx + 1} ({variant})");
            moveBitNoTt.Notation.Should().Be(moveArrNoTt.Notation, $"No-TT best move mismatch on Pos #{idx + 1} ({variant})");
            bitNoTt.LastAnalysis!.Value.Should().Be(arrNoTt.LastAnalysis!.Value, $"No-TT score mismatch on Pos #{idx + 1} ({variant})");

            // 2. With Transposition Table (Depth 6)
            var ttArr = new TranspositionTable(megabytes: 4);
            var ttBit = new TranspositionTable(megabytes: 4);

            var arrWithTt = new MinimaxPlayer(
                depth: 6,
                ruleEngine: arrayEngine,
                evaluator: new EvaluationFunction(variant),
                transpositionTable: ttArr,
                useTranspositionTable: true,
                useQuiescence: true);
            var bitWithTt = new BitboardMinimaxPlayer(
                depth: 6,
                variant: variant,
                transpositionTable: ttBit,
                useTranspositionTable: true,
                useQuiescence: true);

            var moveArrTt = await arrWithTt.GetMoveAsync(state, legalMoves);
            var moveBitTt = await bitWithTt.GetMoveAsync(state, legalMoves);

            bitWithTt.NodesEvaluated.Should().Be(arrWithTt.NodesEvaluated, $"TT node count mismatch on Pos #{idx + 1} ({variant})");
            bitWithTt.LeafEvaluations.Should().Be(arrWithTt.LeafEvaluations, $"TT leaf evals mismatch on Pos #{idx + 1} ({variant})");
            moveBitTt.Notation.Should().Be(moveArrTt.Notation, $"TT best move mismatch on Pos #{idx + 1} ({variant})");
            bitWithTt.LastAnalysis!.Value.Should().Be(arrWithTt.LastAnalysis!.Value, $"TT score mismatch on Pos #{idx + 1} ({variant})");
            ttBit.Cutoffs.Should().Be(ttArr.Cutoffs, $"TT cutoffs mismatch on Pos #{idx + 1} ({variant})");
        }
    }

    [Fact]
    public void BenchmarkSuite_All40Positions_HaveMultipleLegalMovesInBothInternationalAndEnglish()
    {
        var positions = BenchmarkSuite.GetPositions();
        var intlEngine = new BitboardRuleEngine(CheckersVariant.International);
        var engEngine = new BitboardRuleEngine(CheckersVariant.English);
        var seenHashes = new HashSet<ulong>();

        positions.Count.Should().Be(40);

        foreach (var pos in positions)
        {
            seenHashes.Add(pos.State.ZobristHash).Should().BeTrue($"Position #{pos.Id} must be unique");
            intlEngine.GetLegalMoves(pos.State).Count.Should().BeGreaterThanOrEqualTo(2,
                $"Position #{pos.Id} ({pos.Category}) must have >= 2 legal moves in International");
            engEngine.GetLegalMoves(pos.State).Count.Should().BeGreaterThanOrEqualTo(2,
                $"Position #{pos.Id} ({pos.Category}) must have >= 2 legal moves in English");
        }
    }

    private static long PerftArray(RuleEngine engine, BoardState state, int depth)
    {
        if (depth == 0)
            return 1;

        var moves = engine.GetLegalMoves(state);
        if (depth == 1)
            return moves.Count;

        long nodes = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            var next = engine.ApplyMove(state, moves[i]);
            nodes += PerftArray(engine, next, depth - 1);
        }
        return nodes;
    }

    private static long PerftBitboard(in BitPosition pos, CheckersVariant variant, int depth)
    {
        if (depth == 0)
            return 1;

        Span<BitMove> moves = stackalloc BitMove[128];
        int count = BitboardMoveGenerator.Generate(in pos, variant, moves);
        if (depth == 1)
            return count;

        long nodes = 0;
        for (int i = 0; i < count; i++)
        {
            BitPosition next = pos.Apply(in moves[i]);
            nodes += PerftBitboard(in next, variant, depth - 1);
        }
        return nodes;
    }
}

public class BitboardRegularMoveTests : RegularMoveTests
{
    public BitboardRegularMoveTests() : base(new BitboardRuleEngine()) { }
}

public class BitboardMandatoryCaptureTests : MandatoryCaptureTests
{
    public BitboardMandatoryCaptureTests() : base(new BitboardRuleEngine()) { }
}

public class BitboardMultiJumpTests : MultiJumpTests
{
    public BitboardMultiJumpTests() : base(new BitboardRuleEngine()) { }
}

public class BitboardPromotionTests : PromotionTests
{
    public BitboardPromotionTests() : base(new BitboardRuleEngine()) { }
}

public class BitboardTerminalConditionTests : TerminalConditionTests
{
    public BitboardTerminalConditionTests() : base(new BitboardRuleEngine()) { }
}

public class BitboardFlyingKingTests : FlyingKingTests
{
    public BitboardFlyingKingTests() : base(new BitboardRuleEngine()) { }
}

public class BitboardEnglishCheckersTests : EnglishCheckersTests
{
    public BitboardEnglishCheckersTests() : base(new BitboardRuleEngine(CheckersVariant.English)) { }
}
