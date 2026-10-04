using Checkers.Core.AI;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;
using Xunit;

namespace Checkers.Core.Tests;

public class EnglishCheckersTests
{
    private readonly IRuleEngine _engine;

    public EnglishCheckersTests() : this(new RuleEngine(CheckersVariant.English)) { }

    protected EnglishCheckersTests(IRuleEngine engine) => _engine = engine;

    [Fact]
    public void EnglishKing_QuietMoves_CanMoveInAllFourDirections_ExactlyOneSquare()
    {
        // Dark square D4: Row 4, Col 3 (4+3 = 7, odd -> dark square)
        var state = BoardState.CreateEmpty();
        var kingPos = new Position(4, 3);
        state.SetPiece(kingPos, Piece.WhiteKing);
        state.ActivePlayer = PieceColor.White;

        var moves = _engine.GetLegalMoves(state);

        // Should have exactly 4 diagonal single-step moves
        moves.Should().HaveCount(4);
        moves.Should().OnlyContain(m => !m.IsCapture);
        moves.Select(m => m.To).Should().BeEquivalentTo(new[]
        {
            new Position(3, 2),
            new Position(3, 4),
            new Position(5, 2),
            new Position(5, 4)
        });
    }

    [Fact]
    public void EnglishKing_CannotFlyAcrossEmptyBoard()
    {
        var state = BoardState.CreateEmpty();
        var kingPos = new Position(4, 3);
        state.SetPiece(kingPos, Piece.WhiteKing);
        state.ActivePlayer = PieceColor.White;

        var moves = _engine.GetLegalMoves(state);

        // Distance 2 or greater moves must NOT exist (flying king behavior prohibited)
        moves.Should().NotContain(m => m.To == new Position(2, 1));
        moves.Should().NotContain(m => m.To == new Position(1, 0));
        moves.Should().NotContain(m => m.To == new Position(6, 5));
    }

    [Fact]
    public void EnglishKing_SingleHopCapture_ForwardAndBackward()
    {
        var state = BoardState.CreateEmpty();
        var kingPos = new Position(4, 3);
        state.SetPiece(kingPos, Piece.WhiteKing);

        // Enemy man forward-right at (3, 4), landing at (2, 5)
        state.SetPiece(new Position(3, 4), Piece.BlackMan);

        // Enemy man backward-left at (5, 2), landing at (6, 1)
        state.SetPiece(new Position(5, 2), Piece.BlackMan);

        state.ActivePlayer = PieceColor.White;

        var moves = _engine.GetLegalMoves(state);

        // Compulsory capture: must return only the 2 capture options
        moves.Should().HaveCount(2);
        moves.Should().OnlyContain(m => m.IsCapture);
        moves.Select(m => m.To).Should().BeEquivalentTo(new[]
        {
            new Position(2, 5),
            new Position(6, 1)
        });
    }

    [Fact]
    public void EnglishKing_CannotFlyToCaptureOrLandBeyondImmediateSquare()
    {
        var state = BoardState.CreateEmpty();
        var kingPos = new Position(5, 2);
        state.SetPiece(kingPos, Piece.WhiteKing);

        // Enemy piece at (3, 4) - distance 2 away with (4, 3) empty
        state.SetPiece(new Position(3, 4), Piece.BlackMan);
        state.ActivePlayer = PieceColor.White;

        var moves = _engine.GetLegalMoves(state);

        // Cannot jump an enemy at distance 2 (not adjacent)
        moves.Should().NotContain(m => m.IsCapture);

        // Now test landing: place enemy adjacent at (4, 3). Empty squares behind at (3, 4) and (2, 5).
        state = BoardState.CreateEmpty();
        state.SetPiece(kingPos, Piece.WhiteKing);
        state.SetPiece(new Position(4, 3), Piece.BlackMan);
        state.ActivePlayer = PieceColor.White;

        moves = _engine.GetLegalMoves(state);

        // Must ONLY land on the immediate square behind: (3, 4). Cannot fly to (2, 5) or (1, 6).
        moves.Should().ContainSingle(m => m.To == new Position(3, 4));
        moves.Should().NotContain(m => m.To == new Position(2, 5));
        moves.Should().NotContain(m => m.To == new Position(1, 6));
    }

    [Fact]
    public void EnglishKing_MultiJump_CanChainInAllFourDirections()
    {
        var state = BoardState.CreateEmpty();
        var kingPos = new Position(5, 2);
        state.SetPiece(kingPos, Piece.WhiteKing);

        // First jump: jump enemy at (4, 3), land at (3, 4)
        state.SetPiece(new Position(4, 3), Piece.BlackMan);

        // Second jump from (3, 4): jump enemy backward-right at (4, 5), land at (5, 6)
        state.SetPiece(new Position(4, 5), Piece.BlackMan);

        state.ActivePlayer = PieceColor.White;

        var moves = _engine.GetLegalMoves(state);

        // Must complete the multi-jump chain to (5, 6)
        moves.Should().ContainSingle();
        var move = moves[0];
        move.From.Should().Be(kingPos);
        move.To.Should().Be(new Position(5, 6));
        move.CapturedPositions.Should().BeEquivalentTo(new[]
        {
            new Position(4, 3),
            new Position(4, 5)
        });
        move.Path.Should().Equal(new[]
        {
            kingPos,
            new Position(3, 4),
            new Position(5, 6)
        });
    }

    [Fact]
    public void EnglishCheckers_MenCannotMoveOrCaptureBackward()
    {
        var state = BoardState.CreateEmpty();
        var manPos = new Position(4, 3);
        state.SetPiece(manPos, Piece.WhiteMan);

        // Enemy behind at (5, 2) with (6, 1) empty
        state.SetPiece(new Position(5, 2), Piece.BlackMan);
        state.ActivePlayer = PieceColor.White;

        var moves = _engine.GetLegalMoves(state);

        // Man cannot capture backward
        moves.Should().NotContain(m => m.IsCapture);

        // Man can only move forward diagonally: (3, 2) and (3, 4)
        moves.Should().HaveCount(2);
        moves.Select(m => m.To).Should().BeEquivalentTo(new[]
        {
            new Position(3, 2),
            new Position(3, 4)
        });
    }

    [Fact]
    public void EnglishCheckers_MandatoryCaptureEnforced()
    {
        var state = BoardState.CreateEmpty();
        // White King with capture
        state.SetPiece(new Position(4, 3), Piece.WhiteKing);
        state.SetPiece(new Position(3, 4), Piece.BlackMan);

        // White Man with quiet move
        state.SetPiece(new Position(6, 1), Piece.WhiteMan);
        state.ActivePlayer = PieceColor.White;

        var moves = _engine.GetLegalMoves(state);

        // Only captures are legal
        moves.Should().OnlyContain(m => m.IsCapture);
        moves.Should().NotContain(m => m.From == new Position(6, 1));
    }

    [Fact]
    public void EnglishCheckers_Evaluation_ValuesKingAndScoresCentralization()
    {
        var legacyEnglishEval = new LegacyEvaluationFunction(CheckersVariant.English);
        var legacyIntlEval = new LegacyEvaluationFunction(CheckersVariant.International);
        var englishEval = new EvaluationFunction(CheckersVariant.English);
        var intlEval = new EvaluationFunction(CheckersVariant.International);

        // 1 King in center (4, 3)
        var state = BoardState.CreateEmpty();
        state.SetPiece(new Position(4, 3), Piece.WhiteKing);
        state.ActivePlayer = PieceColor.White;

        // Legacy evaluator:
        // English king: 170 (material) + 12 (center control) + 10 (king centralization) = 192
        legacyEnglishEval.Evaluate(state).Should().Be(170 + 12 + 10);
        // International king: 300 (material) + 12 (center control) = 312
        legacyIntlEval.Evaluate(state).Should().Be(300 + 12);

        // New evaluator (2x scaled board_eval.c):
        // English king at (4, 3): 140 (material) + 8 (EnglishKingPst4Mask) + 400 (CalculatePieceBonus(1)) = 548
        englishEval.Evaluate(state).Should().Be(140 + 8 + 400);
        // International Flying King at (4, 3): 300 (material) + 12 (CenterMask) + 12 (FlyingKingMainDiagonalMask) + 400 (trade bonus vs 0 enemy pieces) = 724
        intlEval.Evaluate(state).Should().Be(300 + 12 + 12 + 400);
    }

    [Fact]
    public async Task EnglishCheckers_MinimaxAI_SelectsValidLegalMove()
    {
        var engine = new RuleEngine(CheckersVariant.English);
        var ai = new MinimaxPlayer(depth: 3, ruleEngine: engine, evaluator: new EvaluationFunction(CheckersVariant.English));

        var state = BoardState.CreateInitial();
        var legalMoves = engine.GetLegalMoves(state);

        var move = await ai.GetMoveAsync(state, legalMoves);

        legalMoves.Should().Contain(m => m.From == move.From && m.To == move.To);
    }
}
