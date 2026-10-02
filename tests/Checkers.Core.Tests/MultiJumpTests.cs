using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class MultiJumpTests
{
    private readonly RuleEngine _engine = new();

    [Fact]
    public void DoubleJump_ExecutesZigZagAndRemovesBothPieces()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);

        // White at (5, 2)
        var whitePos = new Position(5, 2);
        board.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));

        // Black 1 at (4, 3) -> White jumps to (3, 4)
        var black1 = new Position(4, 3);
        board.SetPiece(black1, new Piece(PieceColor.Black, PieceType.Man));

        // Black 2 at (2, 3) -> From (3, 4), White jumps to (1, 2)
        var black2 = new Position(2, 3);
        board.SetPiece(black2, new Piece(PieceColor.Black, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        moves.Should().HaveCount(1);
        var move = moves[0];
        move.From.Should().Be(whitePos);
        move.To.Should().Be(new Position(1, 2));
        move.CapturedPositions.Should().BeEquivalentTo(new[] { black1, black2 });
        move.Path.Should().Equal(new[] { whitePos, new Position(3, 4), new Position(1, 2) });

        // Apply the move
        var nextState = _engine.ApplyMove(board, move);

        nextState.GetPiece(whitePos).Should().BeNull();
        nextState.GetPiece(black1).Should().BeNull();
        nextState.GetPiece(black2).Should().BeNull();
        nextState.GetPiece(new Position(1, 2)).Should().Be(new Piece(PieceColor.White, PieceType.Man));
        nextState.BlackPiecesCount.Should().Be(0);
        nextState.WhitePiecesCount.Should().Be(1);
        nextState.ActivePlayer.Should().Be(PieceColor.Black);
    }

    [Fact]
    public void MultiJump_Branching_OffersBothAlternativeChains()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);

        // White at (5, 4)
        var whitePos = new Position(5, 4);
        board.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));

        // Black 1 at (4, 3) -> White lands at (3, 2)
        var black1 = new Position(4, 3);
        board.SetPiece(black1, new Piece(PieceColor.Black, PieceType.Man));

        // From (3, 2), White can jump:
        // Option A: Jump Black at (2, 1) -> lands at (1, 0)
        var black2A = new Position(2, 1);
        board.SetPiece(black2A, new Piece(PieceColor.Black, PieceType.Man));

        // Option B: Jump Black at (2, 3) -> lands at (1, 4)
        var black2B = new Position(2, 3);
        board.SetPiece(black2B, new Piece(PieceColor.Black, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // Both branching paths must be generated as valid complete moves
        moves.Should().HaveCount(2);
        moves.Should().Contain(m => m.To == new Position(1, 0) && m.CapturedPositions.Contains(black2A));
        moves.Should().Contain(m => m.To == new Position(1, 4) && m.CapturedPositions.Contains(black2B));
    }
}
