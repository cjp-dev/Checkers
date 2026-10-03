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
    public void BitboardMasks_DarkSquaresAndRowColMasks_MatchBoardGeometry()
    {
        BitOperations.PopCount(BitboardMasks.DarkSquares).Should().Be(32);

        for (int r = 0; r < 8; r++)
        {
            BitOperations.PopCount(BitboardMasks.Rows[r]).Should().Be(8);

            for (int c = 0; c < 8; c++)
            {
                int sq = BitboardMasks.ToSquareIndex(r, c);
                ulong bit = 1UL << sq;
                var pos = new Position(r, c);

                BitboardMasks.Positions[sq].Should().Be(pos);
                ((BitboardMasks.DarkSquares & bit) != 0).Should().Be(pos.IsDarkSquare);
                ((BitboardMasks.NotColA & bit) != 0).Should().Be(c != 0);
                ((BitboardMasks.NotColH & bit) != 0).Should().Be(c != 7);
                ((BitboardMasks.NotColAB & bit) != 0).Should().Be(c >= 2);
                ((BitboardMasks.NotColGH & bit) != 0).Should().Be(c <= 5);
                ((BitboardMasks.Rows[r] & bit) != 0).Should().BeTrue();
                ((BitboardMasks.CenterMask & bit) != 0).Should().Be(pos.IsDarkSquare && r is >= 3 and <= 4 && c is >= 2 and <= 5);
                ((BitboardMasks.KingCenterMask & bit) != 0).Should().Be(pos.IsDarkSquare && r is >= 2 and <= 5 && c is >= 2 and <= 5);
            }
        }
    }

    [Fact]
    public void BitboardMasks_DiagonalRays_MatchStepByStepOffsets()
    {
        int[] dRows = [-1, -1, 1, 1];
        int[] dCols = [-1, 1, -1, 1];

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                if (((r + c) & 1) == 0)
                    continue;

                int sq = BitboardMasks.ToSquareIndex(r, c);
                for (int dir = 0; dir < 4; dir++)
                {
                    ulong expectedRay = 0UL;
                    int nr = r + dRows[dir];
                    int nc = c + dCols[dir];
                    while (nr is >= 0 and < 8 && nc is >= 0 and < 8)
                    {
                        expectedRay |= 1UL << BitboardMasks.ToSquareIndex(nr, nc);
                        nr += dRows[dir];
                        nc += dCols[dir];
                    }

                    BitboardMasks.Rays[dir, sq].Should().Be(expectedRay);
                }
            }
        }
    }

    [Fact]
    public void BitPosition_FromBoardStateAndToBoardState_RoundTripsLosslessly()
    {
        var initial = BoardState.CreateInitial();
        var bitPos = BitPosition.FromBoardState(initial);

        BitOperations.PopCount(bitPos.WhiteMen).Should().Be(12);
        BitOperations.PopCount(bitPos.BlackMen).Should().Be(12);
        bitPos.WhiteKings.Should().Be(0UL);
        bitPos.BlackKings.Should().Be(0UL);
        bitPos.Hash.Should().Be(initial.ZobristHash);

        var roundTripped = bitPos.ToBoardState(initial.FullMoveNumber);
        roundTripped.ZobristHash.Should().Be(initial.ZobristHash);
        roundTripped.WhitePiecesCount.Should().Be(12);
        roundTripped.BlackPiecesCount.Should().Be(12);

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                roundTripped.GetPiece(r, c).Should().Be(initial.GetPiece(r, c));
            }
        }
    }

    [Theory]
    [InlineData(CheckersVariant.International)]
    [InlineData(CheckersVariant.English)]
    public void RandomSelfPlay_100Games_IncrementalHashAndMoveGenerationAreConsistent(CheckersVariant variant)
    {
        var engine = new RuleEngine(variant);
        var evaluator = new EvaluationFunction(variant);
        Span<BitMove> moveBuffer = stackalloc BitMove[128];

        for (int game = 0; game < 50; game++)
        {
            var rng = new Random(10_000 + game * 97 + (int)variant * 100_000);
            var state = BoardState.CreateInitial();
            var bitPos = state.BitPosition;

            for (int ply = 0; ply < 120; ply++)
            {
                bitPos.Hash.Should().Be(state.ZobristHash, $"Zobrist hash mismatch at game {game}, ply {ply} ({variant})");

                var recalc = bitPos;
                recalc.RecalculateHash().Should().Be(bitPos.Hash, $"Incremental hash must equal full RecalculateHash at game {game}, ply {ply}");

                EvaluationFunction.Evaluate(in bitPos, variant).Should().Be(
                    evaluator.Evaluate(state),
                    $"Evaluation mismatch at game {game}, ply {ply} ({variant})");

                var moves = engine.GetLegalMoves(state);
                int bitMoveCount = BitboardMoveGenerator.Generate(in bitPos, variant, moveBuffer);
                bool hasAny = BitboardMoveGenerator.HasAnyLegalMove(in bitPos, variant);

                hasAny.Should().Be(moves.Count > 0, $"HasAnyLegalMove mismatch at game {game}, ply {ply} ({variant})");
                bitMoveCount.Should().Be(moves.Count, $"Move count mismatch at game {game}, ply {ply} ({variant})");

                if (moves.Count == 0)
                    break;

                for (int i = 0; i < moves.Count; i++)
                {
                    BitMove expectedBitMove = BitMove.FromMove(moves[i]);
                    moveBuffer[i].Should().Be(expectedBitMove);
                }

                int idx = rng.Next(moves.Count);
                state = engine.ApplyMove(state, moves[idx]);
                bitPos = bitPos.Apply(in moveBuffer[idx]);
            }
        }
    }

    [Theory]
    [InlineData(CheckersVariant.International, 1, 7L)]
    [InlineData(CheckersVariant.International, 2, 49L)]
    [InlineData(CheckersVariant.International, 3, 302L)]
    [InlineData(CheckersVariant.International, 4, 1469L)]
    [InlineData(CheckersVariant.International, 5, 7361L)]
    [InlineData(CheckersVariant.English, 1, 7L)]
    [InlineData(CheckersVariant.English, 2, 49L)]
    [InlineData(CheckersVariant.English, 3, 302L)]
    [InlineData(CheckersVariant.English, 4, 1469L)]
    [InlineData(CheckersVariant.English, 5, 7361L)]
    public void Perft_InitialPosition_MatchesRuleEngineAndExpectedLeafNodes(
        CheckersVariant variant,
        int depth,
        long expectedLeafNodes)
    {
        var engine = new RuleEngine(variant);
        var initial = BoardState.CreateInitial();
        var bitPos = initial.BitPosition;

        long engineNodes = PerftRuleEngine(engine, initial, depth);
        long bitboardNodes = PerftBitboard(in bitPos, variant, depth);

        bitboardNodes.Should().Be(engineNodes);
        bitboardNodes.Should().Be(expectedLeafNodes);
    }

    [Theory]
    [InlineData(CheckersVariant.International)]
    [InlineData(CheckersVariant.English)]
    public async Task MinimaxPlayer_WithAndWithoutTT_ProducesDeterministicSearchAndCutoffs(CheckersVariant variant)
    {
        var positions = BenchmarkSuite.GetPositions();
        var sampleIndices = new[] { 0, 5, 12, 18, 26, 33, 38 };

        foreach (int idx in sampleIndices)
        {
            var state = positions[idx].State;
            var engine = new RuleEngine(variant);
            var legalMoves = engine.GetLegalMoves(state);

            var noTt1 = new MinimaxPlayer(
                depth: 5,
                variant: variant,
                useTranspositionTable: false,
                useQuiescence: true);
            var noTt2 = new MinimaxPlayer(
                depth: 5,
                variant: variant,
                useTranspositionTable: false,
                useQuiescence: true);

            var move1 = await noTt1.GetMoveAsync(state, legalMoves);
            var move2 = await noTt2.GetMoveAsync(state, legalMoves);

            noTt1.NodesEvaluated.Should().Be(noTt2.NodesEvaluated);
            noTt1.LeafEvaluations.Should().Be(noTt2.LeafEvaluations);
            move1.Notation.Should().Be(move2.Notation);

            var tt = new TranspositionTable(megabytes: 4);
            var withTt = new MinimaxPlayer(
                depth: 6,
                variant: variant,
                transpositionTable: tt,
                useTranspositionTable: true,
                useQuiescence: true);

            var moveTt = await withTt.GetMoveAsync(state, legalMoves);
            legalMoves.Should().Contain(m => m.Notation == moveTt.Notation);
            withTt.NodesEvaluated.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void BenchmarkSuite_All40Positions_HaveMultipleLegalMovesInBothInternationalAndEnglish()
    {
        var positions = BenchmarkSuite.GetPositions();
        var intlEngine = new RuleEngine(CheckersVariant.International);
        var engEngine = new RuleEngine(CheckersVariant.English);
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

    private static long PerftRuleEngine(RuleEngine engine, BoardState state, int depth)
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
            nodes += PerftRuleEngine(engine, next, depth - 1);
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
