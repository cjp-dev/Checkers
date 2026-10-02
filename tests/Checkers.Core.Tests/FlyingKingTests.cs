using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class FlyingKingTests
{
    private readonly RuleEngine _engine = new();

    [Fact]
    public void FlyingKing_CanSlideAcrossMultipleEmptySquares()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        // Place White King at (3, 2)
        var kingPos = new Position(3, 2);
        board.SetPiece(kingPos, new Piece(PieceColor.White, PieceType.King));

        var moves = _engine.GetLegalMoves(board);

        // Expected diagonals from (3,2):
        // Up-Left: (2,1), (1,0) [2]
        // Up-Right: (2,3), (1,4), (0,5) [3]
        // Down-Left: (4,1), (5,0) [2]
        // Down-Right: (4,3), (5,4), (6,5), (7,6) [4]
        // Total = 11 moves.
        moves.Should().HaveCount(11);
        moves.Should().AllSatisfy(m =>
        {
            m.From.Should().Be(kingPos);
            m.IsCapture.Should().BeFalse();
        });
    }

    [Fact]
    public void FlyingKing_BlockedByFriendlyPiece_CannotSlidePast()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        var kingPos = new Position(3, 2);
        board.SetPiece(kingPos, new Piece(PieceColor.White, PieceType.King));

        // Friendly piece at (1, 4) blocks up-right diagonal after (2, 3)
        var friendlyPos = new Position(1, 4);
        board.SetPiece(friendlyPos, new Piece(PieceColor.White, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // King can reach (2,3), but not (1,4) or (0,5)
        moves.Should().Contain(m => m.From == kingPos && m.To == new Position(2, 3));
        moves.Should().NotContain(m => m.From == kingPos && m.To == friendlyPos);
        moves.Should().NotContain(m => m.From == kingPos && m.To == new Position(0, 5));
    }

    [Fact]
    public void FlyingKing_LongDistanceJump_CanChooseAnyLandingSquareBehindEnemy()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);

        // White King at (6, 1)
        var kingPos = new Position(6, 1);
        board.SetPiece(kingPos, new Piece(PieceColor.White, PieceType.King));

        // Black piece at (4, 3) - square (5, 2) is empty in between
        var blackPos = new Position(4, 3);
        board.SetPiece(blackPos, new Piece(PieceColor.Black, PieceType.Man));

        // Squares behind Black piece: (3, 4), (2, 5), (1, 6), (0, 7)
        var moves = _engine.GetLegalMoves(board);

        // King must capture and can land on any of the 4 open squares along that diagonal
        moves.Should().HaveCount(4);
        moves.Should().AllSatisfy(m =>
        {
            m.From.Should().Be(kingPos);
            m.IsCapture.Should().BeTrue();
            m.CapturedPositions.Should().ContainSingle().Which.Should().Be(blackPos);
        });

        moves.Select(m => m.To).Should().BeEquivalentTo(new[]
        {
            new Position(3, 4),
            new Position(2, 5),
            new Position(1, 6),
            new Position(0, 7)
        });
    }

    [Fact]
    public void FlyingKing_MultiJump_ChainsAcrossDifferentDiagonals()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);

        // King at (7, 0)
        var kingPos = new Position(7, 0);
        board.SetPiece(kingPos, new Piece(PieceColor.White, PieceType.King));

        // Enemy 1 at (5, 2) -> King can land at (4, 3)
        var black1 = new Position(5, 2);
        board.SetPiece(black1, new Piece(PieceColor.Black, PieceType.Man));

        // Enemy 2 at (2, 5) -> From (4, 3), King can jump enemy 2 and land at (1, 6) or (0, 7)
        var black2 = new Position(2, 5);
        board.SetPiece(black2, new Piece(PieceColor.Black, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // There are 2 intermediate landing squares (4,3) and (3,4), and for each,
        // 2 terminal landing squares (1,6) and (0,7) -> 4 distinct paths.
        moves.Should().HaveCount(4);
        moves.Should().AllSatisfy(m =>
        {
            m.From.Should().Be(kingPos);
            m.CapturedPositions.Should().BeEquivalentTo(new[] { black1, black2 });
        });

        moves.Select(m => m.To).Distinct().Should().BeEquivalentTo(new[]
        {
            new Position(1, 6),
            new Position(0, 7)
        });

        var chosenMove = moves.First(m => m.To == new Position(0, 7));
        var nextState = _engine.ApplyMove(board, chosenMove);

        nextState.GetPiece(kingPos).Should().BeNull();
        nextState.GetPiece(black1).Should().BeNull();
        nextState.GetPiece(black2).Should().BeNull();
        nextState.GetPiece(new Position(0, 7))!.Value.IsKing.Should().BeTrue();
        nextState.BlackPiecesCount.Should().Be(0);
    }

    [Fact]
    public void FlyingKing_MultiJump_TurnsCornerAcrossPerpendicularDiagonals()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);

        // White King at (7, 0)
        var kingPos = new Position(7, 0);
        board.SetPiece(kingPos, new Piece(PieceColor.White, PieceType.King));

        // Jump 1: Up-Right. Enemy at (5, 2) -> landing at (3, 4)
        var black1 = new Position(5, 2);
        board.SetPiece(black1, new Piece(PieceColor.Black, PieceType.Man));

        // Jump 2: Turn Up-Left from (3, 4). Enemy at (2, 3) -> landing at (1, 2) or (0, 1)
        var black2 = new Position(2, 3);
        board.SetPiece(black2, new Piece(PieceColor.Black, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // Should include jumps that captured both black1 and black2
        moves.Should().NotBeEmpty();
        moves.Should().AllSatisfy(m =>
        {
            m.IsCapture.Should().BeTrue();
            m.CapturedPositions.Should().Contain(black1);
            m.CapturedPositions.Should().Contain(black2);
        });

        var cornerMove = moves.First(m => m.To == new Position(0, 1));
        var nextState = _engine.ApplyMove(board, cornerMove);

        nextState.GetPiece(new Position(0, 1))!.Value.IsKing.Should().BeTrue();
        nextState.GetPiece(black1).Should().BeNull();
        nextState.GetPiece(black2).Should().BeNull();
        nextState.BlackPiecesCount.Should().Be(0);
    }

    [Fact]
    public void ScreenshotBoard_AnalyzeLegalMoves()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(Position.FromDraughtsIndex(2), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(Position.FromDraughtsIndex(20), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(Position.FromDraughtsIndex(28), new Piece(PieceColor.White, PieceType.Man));
        board.SetPiece(Position.FromDraughtsIndex(29), new Piece(PieceColor.White, PieceType.Man));
        board.SetPiece(Position.FromDraughtsIndex(31), new Piece(PieceColor.White, PieceType.Man));

        board.SetPiece(Position.FromDraughtsIndex(1), new Piece(PieceColor.Black, PieceType.Man));
        board.SetPiece(Position.FromDraughtsIndex(11), new Piece(PieceColor.Black, PieceType.Man));
        board.SetPiece(Position.FromDraughtsIndex(14), new Piece(PieceColor.Black, PieceType.King));
        board.SetPiece(Position.FromDraughtsIndex(19), new Piece(PieceColor.Black, PieceType.Man));
        board.SetPiece(Position.FromDraughtsIndex(22), new Piece(PieceColor.Black, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);
        var movesFrom2 = moves.Where(m => m.From == Position.FromDraughtsIndex(2)).ToList();
        var movesFrom20 = moves.Where(m => m.From == Position.FromDraughtsIndex(20)).ToList();

        // All legal moves are captures (strict mandatory capture)
        moves.Should().AllSatisfy(m => m.IsCapture.Should().BeTrue());

        // Piece 2 has 5 terminal landing squares across its capture paths: 3, 5, 7, 9, 10
        movesFrom2.Select(m => m.To.ToDraughtsIndex()).Distinct().Should().BeEquivalentTo(new int?[] { 3, 5, 7, 9, 10 });

        // Piece 20 has 2 terminal landing squares: 12, 16
        movesFrom20.Select(m => m.To.ToDraughtsIndex()).Distinct().Should().BeEquivalentTo(new int?[] { 12, 16 });
    }
}

