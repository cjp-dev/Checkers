using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class RegularMoveTests
{
    private readonly RuleEngine _engine = new();

    [Fact]
    public void InitialBoard_WhiteHasSevenValidOpeningMoves()
    {
        var board = BoardState.CreateInitial();
        var moves = _engine.GetLegalMoves(board);

        // On starting board:
        // Pieces on row 5 (indices 21, 22, 23, 24) can move forward to row 4:
        // 21 (5,0) -> 17 (4,1) [1 move]
        // 22 (5,2) -> 17 (4,1) or 18 (4,3) [2 moves]
        // 23 (5,4) -> 18 (4,3) or 19 (4,5) [2 moves]
        // 24 (5,6) -> 19 (4,5) or 20 (4,7) [2 moves]
        // Total = 7 moves.
        moves.Should().HaveCount(7);
        moves.Should().AllSatisfy(m =>
        {
            m.IsCapture.Should().BeFalse();
            m.IsPromotion.Should().BeFalse();
            m.From.Row.Should().Be(5);
            m.To.Row.Should().Be(4);
        });
    }

    [Fact]
    public void Men_CannotMoveBackwards()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        var whitePos = new Position(4, 3); // dark square #18
        board.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // White can only move forward (row 3), not backward (row 5)
        moves.Should().HaveCount(2);
        moves.Select(m => m.To).Should().BeEquivalentTo(new[]
        {
            new Position(3, 2),
            new Position(3, 4)
        });
    }

    [Fact]
    public void Men_BlockedByFriendlyPiece_CannotMove()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        var whitePos = new Position(4, 3);
        board.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));
        // Block both forward diagonals
        board.SetPiece(new Position(3, 2), new Piece(PieceColor.White, PieceType.Man));
        board.SetPiece(new Position(3, 4), new Piece(PieceColor.White, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);
        // Only the two blocking pieces at row 3 might move (to row 2)
        moves.Should().NotContain(m => m.From == whitePos);
    }
}
