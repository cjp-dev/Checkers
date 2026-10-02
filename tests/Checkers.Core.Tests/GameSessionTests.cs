using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class GameSessionTests
{
    [Fact]
    public void StartNewGame_InitializesProperly()
    {
        var session = new GameSession();

        session.Status.Should().Be(GameStatus.InProgress);
        session.GameOverReason.Should().Be(GameOverReason.None);
        session.CurrentState.ActivePlayer.Should().Be(PieceColor.White);
        session.LegalMoves.Should().HaveCount(7);
        session.MoveHistory.Should().BeEmpty();
        session.CanUndo.Should().BeFalse();
        session.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void TryMakeMove_ExecutesValidMoveAndFiresEvent()
    {
        var session = new GameSession();
        var move = session.LegalMoves[0];

        bool eventFired = false;
        session.MoveExecuted += (s, e) =>
        {
            eventFired = true;
            e.Move.Should().Be(move);
        };

        bool success = session.TryMakeMove(move);

        success.Should().BeTrue();
        eventFired.Should().BeTrue();
        session.MoveHistory.Should().ContainSingle().Which.Should().Be(move);
        session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);
        session.CanUndo.Should().BeTrue();
        session.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void TryMakeMove_RejectsIllegalMove()
    {
        var session = new GameSession();
        // Construct an illegal move (moving backward or jumping nonexistent piece)
        var illegalMove = Move.CreateQuiet(new Position(7, 0), new Position(6, 1));

        bool success = session.TryMakeMove(illegalMove);

        success.Should().BeFalse();
        session.MoveHistory.Should().BeEmpty();
        session.CurrentState.ActivePlayer.Should().Be(PieceColor.White);
    }

    [Fact]
    public void UndoAndRedo_RestoreStatesAccurately()
    {
        var session = new GameSession();
        var move = session.LegalMoves[0];
        var initialHash = session.CurrentState.ZobristHash;

        session.TryMakeMove(move);
        session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);

        // Undo
        session.CanUndo.Should().BeTrue();
        bool undoSuccess = session.Undo();

        undoSuccess.Should().BeTrue();
        session.CurrentState.ActivePlayer.Should().Be(PieceColor.White);
        session.CurrentState.ZobristHash.Should().Be(initialHash);
        session.MoveHistory.Should().BeEmpty();
        session.CanRedo.Should().BeTrue();

        // Redo
        bool redoSuccess = session.Redo();

        redoSuccess.Should().BeTrue();
        session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);
        session.MoveHistory.Should().ContainSingle().Which.Should().Be(move);
        session.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void GameOver_FiresGameOverEvent()
    {
        var session = new GameSession();

        // Set up a custom board where White can win in 1 move
        var customState = BoardState.CreateEmpty(PieceColor.White);
        // White at (2, 3) can jump Black at (1, 2) and land at (0, 1), capturing Black's only piece
        var whitePos = new Position(2, 3);
        var blackPos = new Position(1, 2);
        customState.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));
        customState.SetPiece(blackPos, new Piece(PieceColor.Black, PieceType.Man));
        customState.RecalculateHash();

        session.StartFromState(customState);

        bool gameOverFired = false;
        GameOverEventArgs? gameOverArgs = null;
        session.GameOver += (s, e) =>
        {
            gameOverFired = true;
            gameOverArgs = e;
        };

        var winningMove = session.LegalMoves.First(m => m.IsCapture);
        session.TryMakeMove(winningMove);

        gameOverFired.Should().BeTrue();
        gameOverArgs.Should().NotBeNull();
        gameOverArgs!.Status.Should().Be(GameStatus.WhiteWon);
        gameOverArgs.Reason.Should().Be(GameOverReason.OpponentPiecesEliminated);
        session.Status.Should().Be(GameStatus.WhiteWon);
    }
}
