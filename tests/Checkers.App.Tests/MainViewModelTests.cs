using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.App.ViewModels;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.App.Tests;

public class MainViewModelTests
{
    private class TestSoundService : ISoundService
    {
        public bool IsEnabled { get; set; } = true;
        public List<SoundType> PlayedSounds { get; } = [];

        public void Play(SoundType sound)
        {
            if (IsEnabled)
                PlayedSounds.Add(sound);
        }
    }

    [Fact]
    public void Constructor_Initializes64SquaresAndPieceCounts()
    {
        var vm = new MainViewModel();

        vm.BoardSquares.Should().HaveCount(64);
        vm.WhitePiecesCount.Should().Be(12);
        vm.BlackPiecesCount.Should().Be(12);
        vm.CanUndo.Should().BeFalse();
        vm.CanRedo.Should().BeFalse();
        vm.StatusText.Should().Contain("White's Turn");
    }

    [Fact]
    public void SquareClicked_SelectsPieceAndHighlightsValidTargets()
    {
        var vm = new MainViewModel();

        // White piece at (5, 0) ( Draughts index 21 ) has 1 quiet move to (4, 1)
        var square = vm.Squares[5, 0];
        vm.SquareClickedCommand.Execute(square);

        square.IsSelected.Should().BeTrue();
        vm.Squares[4, 1].IsValidTarget.Should().BeTrue();
    }

    [Fact]
    public void SquareClicked_ValidTargetExecutesMoveAndPlaysSound()
    {
        var sound = new TestSoundService();
        var vm = new MainViewModel(soundService: sound);

        // Select piece at (5, 0)
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);

        // Click destination (4, 1)
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);

        // Piece should have moved
        vm.Squares[5, 0].HasPiece.Should().BeFalse();
        vm.Squares[4, 1].HasPiece.Should().BeTrue();
        vm.Squares[4, 1].Piece!.Value.Color.Should().Be(PieceColor.White);

        // Turn switched to Black
        vm.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);
        vm.StatusText.Should().Contain("Black's Turn");

        // Audio played
        sound.PlayedSounds.Should().Contain(SoundType.Move);

        // Undo is now enabled
        vm.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void SquareClicked_SelectedPieceAgain_Deselects()
    {
        var vm = new MainViewModel();
        var square = vm.Squares[5, 0];

        // Select
        vm.SquareClickedCommand.Execute(square);
        square.IsSelected.Should().BeTrue();

        // Deselect
        vm.SquareClickedCommand.Execute(square);
        square.IsSelected.Should().BeFalse();
        vm.Squares[4, 1].IsValidTarget.Should().BeFalse();
    }

    [Fact]
    public void UndoAndRedo_CommandsUpdateBoard()
    {
        var vm = new MainViewModel();

        // Make move (5,0) -> (4,1)
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);

        vm.CanUndo.Should().BeTrue();

        // Undo
        vm.UndoCommand.Execute(null);
        vm.Squares[5, 0].HasPiece.Should().BeTrue();
        vm.Squares[4, 1].HasPiece.Should().BeFalse();
        vm.CanRedo.Should().BeTrue();

        // Redo
        vm.RedoCommand.Execute(null);
        vm.Squares[5, 0].HasPiece.Should().BeFalse();
        vm.Squares[4, 1].HasPiece.Should().BeTrue();
    }

    [Fact]
    public void MandatoryCapture_HighlightsCaptureSourceSquare()
    {
        var session = new GameSession();
        var custom = BoardState.CreateEmpty(PieceColor.White);
        // White at (4,3) can capture Black at (3,2) -> (2,1)
        custom.SetPiece(new Position(4, 3), new Piece(PieceColor.White, PieceType.Man));
        custom.SetPiece(new Position(3, 2), new Piece(PieceColor.Black, PieceType.Man));
        session.StartFromState(custom);

        var vm = new MainViewModel(session: session);

        // Square (4,3) should be highlighted as MandatoryCaptureSource
        vm.Squares[4, 3].IsMandatoryCaptureSource.Should().BeTrue();
        vm.StatusText.Should().Contain("Mandatory Jump!");
    }
}
