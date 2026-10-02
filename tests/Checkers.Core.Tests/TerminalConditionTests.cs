using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class TerminalConditionTests
{
    private readonly RuleEngine _engine = new();

    [Fact]
    public void OpponentPiecesEliminated_DeclaresWinner()
    {
        var board = BoardState.CreateEmpty(PieceColor.Black);
        board.SetPiece(new Position(3, 4), new Piece(PieceColor.White, PieceType.King));
        // Black has 0 pieces left

        var (status, reason) = _engine.EvaluateGameStatus(board);

        status.Should().Be(GameStatus.WhiteWon);
        reason.Should().Be(GameOverReason.OpponentPiecesEliminated);
    }

    [Fact]
    public void OpponentBlocked_DeclaresWinner()
    {
        var board = BoardState.CreateEmpty(PieceColor.Black);

        // Black piece trapped at corner (0, 7) ( Draughts index #4 )
        // Normal move for Black is down (+1 row) -> (1, 6)
        var blackPos = new Position(0, 7);
        board.SetPiece(blackPos, new Piece(PieceColor.Black, PieceType.Man));

        // White piece blocks at (1, 6)
        board.SetPiece(new Position(1, 6), new Piece(PieceColor.White, PieceType.Man));

        // White piece behind at (2, 5) prevents Black from jumping (1, 6)
        board.SetPiece(new Position(2, 5), new Piece(PieceColor.White, PieceType.Man));

        var (status, reason) = _engine.EvaluateGameStatus(board);

        // Black has pieces, but 0 legal moves -> White wins
        status.Should().Be(GameStatus.WhiteWon);
        reason.Should().Be(GameOverReason.OpponentNoLegalMoves);
    }

    [Fact]
    public void FortyMoveRule_TriggersDrawAt80HalfMoves()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(new Position(3, 2), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(new Position(7, 6), new Piece(PieceColor.Black, PieceType.King));
        board.HalfMoveClock = 80;

        var (status, reason) = _engine.EvaluateGameStatus(board);

        status.Should().Be(GameStatus.Draw);
        reason.Should().Be(GameOverReason.FortyMoveRuleWithoutCaptureOrPromotion);
    }

    [Fact]
    public void ThreefoldRepetition_TriggersDraw()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(new Position(3, 2), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(new Position(7, 6), new Piece(PieceColor.Black, PieceType.King));
        board.RecalculateHash();

        var hash = board.ZobristHash;
        var history = new List<ulong> { hash, 12345UL, hash, 67890UL, hash };

        var (status, reason) = _engine.EvaluateGameStatus(board, history);

        status.Should().Be(GameStatus.Draw);
        reason.Should().Be(GameOverReason.ThreefoldRepetition);
    }
}
