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

    [Fact]
    public void SetDifficulty_UpdatesAiPlayerType()
    {
        var vm = new MainViewModel();

        vm.SetDifficultyCommand.Execute(AiDifficulty.Easy);
        vm.Difficulty.Should().Be(AiDifficulty.Easy);
        vm.CreateAiPlayer().Should().BeOfType<Checkers.Core.AI.RandomPlayer>();

        vm.SetDifficultyCommand.Execute(AiDifficulty.Hard);
        vm.Difficulty.Should().Be(AiDifficulty.Hard);
        var hardAi = vm.CreateAiPlayer().Should().BeOfType<Checkers.Core.AI.MinimaxPlayer>().Subject;
        hardAi.Depth.Should().Be(6);
    }

    [Fact]
    public async Task HumanVsComputer_TriggersAiTurnAfterHumanMove()
    {
        var vm = new MainViewModel();
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.SetDifficultyCommand.Execute(AiDifficulty.Easy); // fast for testing

        // Human plays (5,0) -> (4,1)
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);

        // Wait up to 1 second for background AI to execute move
        for (int i = 0; i < 20; i++)
        {
            if (vm.Session.MoveHistory.Count >= 2)
                break;
            await Task.Delay(50);
        }

        vm.Session.MoveHistory.Should().HaveCountGreaterThanOrEqualTo(2);
        vm.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.White);
    }

    [Fact]
    public void InitialBoard_MarksOnlyRow5AsMovable()
    {
        var vm = new MainViewModel();

        // Row 5 pieces (draughts 21, 22, 23, 24) are the only ones with forward quiet moves
        vm.Squares[5, 0].IsMovable.Should().BeTrue();
        vm.Squares[5, 0].CanInteract.Should().BeTrue();
        vm.Squares[5, 2].IsMovable.Should().BeTrue();
        vm.Squares[5, 4].IsMovable.Should().BeTrue();
        vm.Squares[5, 6].IsMovable.Should().BeTrue();

        // Row 6 and 7 pieces are blocked by friendly pieces at the start
        vm.Squares[6, 1].IsMovable.Should().BeFalse();
        vm.Squares[6, 1].CanInteract.Should().BeFalse();
        vm.Squares[7, 0].IsMovable.Should().BeFalse();
        vm.Squares[7, 0].CanInteract.Should().BeFalse();
    }

    [Fact]
    public void SquareClicked_BlockedPiece_DisplaysInformativeBlockedMessage()
    {
        var vm = new MainViewModel();

        // Click on blocked piece (6, 1) (draughts 25)
        vm.SquareClickedCommand.Execute(vm.Squares[6, 1]);

        vm.StatusText.Should().Contain("blocked");
        vm.Squares[6, 1].IsSelected.Should().BeFalse();
    }

    [Fact]
    public void SquareClicked_OpponentPiece_DisplaysOpponentWarning()
    {
        var vm = new MainViewModel();

        // Click on black piece (0, 1) during White's turn
        vm.SquareClickedCommand.Execute(vm.Squares[0, 1]);

        vm.StatusText.Should().Contain("Black piece");
        vm.Squares[0, 1].IsSelected.Should().BeFalse();
    }

    [Fact]
    public void TryDragMove_ValidMove_ExecutesMoveAndUpdatesTurn()
    {
        var vm = new MainViewModel();

        // Drag (5, 0) to (4, 1)
        bool result = vm.TryDragMove(vm.Squares[5, 0], vm.Squares[4, 1]);

        result.Should().BeTrue();
        vm.Squares[5, 0].HasPiece.Should().BeFalse();
        vm.Squares[4, 1].HasPiece.Should().BeTrue();
        vm.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);
    }

    [Fact]
    public void TryDragMove_InvalidMove_SelectsSourcePieceIfMovable()
    {
        var vm = new MainViewModel();

        // Drag (5, 0) to invalid square (6, 1)
        bool result = vm.TryDragMove(vm.Squares[5, 0], vm.Squares[6, 1]);

        result.Should().BeFalse();
        vm.Squares[5, 0].IsSelected.Should().BeTrue();
        vm.Squares[4, 1].IsValidTarget.Should().BeTrue();
    }

    [Fact]
    public void GameMode_PropertiesReflectActiveSelection()
    {
        var vm = new MainViewModel();

        // Default mode is HumanVsHuman
        vm.IsHumanVsHuman.Should().BeTrue();
        vm.IsHumanVsComputer.Should().BeFalse();
        vm.IsAiDifficultyEnabled.Should().BeFalse();
        vm.GameModeBadgeText.Should().Be("vs Human");

        // Switch to HumanVsComputer
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.IsHumanVsComputer.Should().BeTrue();
        vm.IsHumanVsHuman.Should().BeFalse();
        vm.IsAiDifficultyEnabled.Should().BeTrue();
        vm.GameModeBadgeText.Should().Be("vs Computer");
        vm.WhitePlayerLabel.Should().Contain("You");
        vm.BlackPlayerLabel.Should().Contain("Computer");
    }

    [Fact]
    public void Difficulty_PropertiesReflectActiveSelection()
    {
        var vm = new MainViewModel();

        // Default is Medium
        vm.IsMediumDifficulty.Should().BeTrue();
        vm.IsEasyDifficulty.Should().BeFalse();
        vm.IsHardDifficulty.Should().BeFalse();
        vm.DifficultyBadgeText.Should().Contain("Medium");

        // Set to Easy
        vm.SetDifficultyCommand.Execute(AiDifficulty.Easy);
        vm.IsEasyDifficulty.Should().BeTrue();
        vm.IsMediumDifficulty.Should().BeFalse();
        vm.DifficultyBadgeText.Should().Contain("Easy");

        // Set to Hard
        vm.SetDifficultyCommand.Execute(AiDifficulty.Hard);
        vm.IsHardDifficulty.Should().BeTrue();
        vm.IsEasyDifficulty.Should().BeFalse();
        vm.DifficultyBadgeText.Should().Contain("Hard");
    }

    [Fact]
    public void SelectPiece_WithCapture_HighlightsCapturedEnemyTargetsAndUpdatesStatus()
    {
        var session = new GameSession();
        var custom = BoardState.CreateEmpty(PieceColor.White);
        // White at (4,3) can capture Black at (3,2) -> (2,1)
        custom.SetPiece(new Position(4, 3), new Piece(PieceColor.White, PieceType.Man));
        custom.SetPiece(new Position(3, 2), new Piece(PieceColor.Black, PieceType.Man));
        session.StartFromState(custom);

        var vm = new MainViewModel(session: session);

        // Select piece at (4, 3)
        vm.SquareClickedCommand.Execute(vm.Squares[4, 3]);

        vm.Squares[4, 3].IsSelected.Should().BeTrue();
        vm.Squares[2, 1].IsValidTarget.Should().BeTrue();
        // The jumped enemy at (3, 2) should be highlighted as CapturedTarget
        vm.Squares[3, 2].IsCapturedTarget.Should().BeTrue();
        vm.StatusText.Should().Contain("captures 1 piece");

        // Hover over landing target (2, 1) shows route preview
        vm.HoverSquare(vm.Squares[2, 1]);
        vm.StatusText.Should().Contain("Route:");

        // Unhover restores status text
        vm.UnhoverSquare(vm.Squares[2, 1]);
        vm.StatusText.Should().Contain("captures 1 piece");
    }
}
