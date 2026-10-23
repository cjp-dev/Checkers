using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.App.ViewModels;
using Checkers.Core.AI;
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

    private class TestDialogService : IDialogService
    {
        public List<string> Errors { get; } = [];
        public GameSettings? SettingsToReturn { get; set; }
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCallCount { get; set; }
        public string? LastConfirmTitle { get; set; }
        public string? LastConfirmMessage { get; set; }
        public void ShowInfo(string title, string message) { }
        public bool ShowConfirmation(string title, string message) => ConfirmResult;
        public Task<bool> ConfirmAsync(string title, string message)
        {
            ConfirmCallCount++;
            LastConfirmTitle = title;
            LastConfirmMessage = message;
            return Task.FromResult(ConfirmResult);
        }
        public void ShowAbout() { }
        public void ShowError(string message) => Errors.Add(message);
        public Task<GameSettings?> EditSettingsAsync(GameSettings current) => Task.FromResult(SettingsToReturn);
    }

    private class TestGameFileService : IGameFileService
    {
        public GameFile? FileToOpen { get; set; }
        public string? SavedPathResult { get; set; } = @"C:\Games\test.checkers";
        public string? LastSavedText { get; private set; }
        public string? LastSavedCurrentPath { get; private set; }
        public bool LastAskForName { get; private set; }

        public Task<GameFile?> OpenAsync() => Task.FromResult(FileToOpen);

        public Task<string?> SaveAsync(string text, string? currentPath, bool askForName)
        {
            LastSavedText = text;
            LastSavedCurrentPath = currentPath;
            LastAskForName = askForName;
            return Task.FromResult(SavedPathResult);
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
    public async Task EditSettingsCommand_UpdatesSettingsAndAiPlayerLimits()
    {
        var dialogs = new TestDialogService
        {
            SettingsToReturn = new GameSettings(TimeControlMode.FixedDepth, 12, 10, 15)
        };
        var vm = new MainViewModel(dialogService: dialogs);

        await vm.EditSettingsCommand.ExecuteAsync(null);

        vm.Settings.Mode.Should().Be(TimeControlMode.FixedDepth);
        vm.Settings.Depth.Should().Be(12);
        vm.SettingsBadgeText.Should().Be("12 plies");
        var player = vm.CreateAiPlayer().Should().BeOfType<MinimaxPlayer>().Subject;
        player.Depth.Should().Be(12);
    }

    [Fact]
    public async Task HumanVsComputer_TriggersAiTurnAfterHumanMove()
    {
        var vm = new MainViewModel();
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.Settings = new GameSettings(TimeControlMode.FixedDepth, 1, 1, 1); // fast for testing

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
        vm.GameModeBadgeText.Should().Be("vs Human");

        // Switch to HumanVsComputer
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.IsHumanVsComputer.Should().BeTrue();
        vm.IsHumanVsHuman.Should().BeFalse();
        vm.GameModeBadgeText.Should().Be("vs Computer");
        vm.WhitePlayerLabel.Should().Be("Human");
        vm.BlackPlayerLabel.Should().Be("Computer");
    }

    [Fact]
    public void Settings_PropertiesAndClockReflectActiveSelection()
    {
        var vm = new MainViewModel();

        // Default settings is TimePerGame 5 min
        vm.Settings.Mode.Should().Be(TimeControlMode.TimePerGame);
        vm.SettingsBadgeText.Should().Be("5 min / game");
        vm.IsClockVisible.Should().BeTrue();
        vm.IsGameSettingVisible.Should().BeFalse(); // Because default mode is HumanVsHuman

        // Switch to HumanVsComputer -> Clock and setting badge become visible
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.IsClockVisible.Should().BeTrue();
        vm.IsGameSettingVisible.Should().BeTrue();
        vm.ClockText.Should().Be("Clock: 5:00");

        // Change to TimePerMove
        vm.Settings = new GameSettings(TimeControlMode.TimePerMove, 8, 10, 5);
        vm.SettingsBadgeText.Should().Be("10s / move");
        vm.IsClockVisible.Should().BeFalse();
        vm.IsGameSettingVisible.Should().BeTrue();

        // Change to FixedDepth
        vm.Settings = new GameSettings(TimeControlMode.FixedDepth, 6, 10, 5);
        vm.SettingsBadgeText.Should().Be("6 plies");
        vm.IsClockVisible.Should().BeFalse();
        vm.IsGameSettingVisible.Should().BeTrue();
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

    [Fact]
    public void ShowAnalysis_DependsOnGameModeAndIsAnalysisVisible()
    {
        var vm = new MainViewModel();

        // Default state: HumanVsHuman, ShowAnalysis is false because not playing computer
        vm.GameMode.Should().Be(GameMode.HumanVsHuman);
        vm.IsAnalysisVisible.Should().BeTrue();
        vm.ShowAnalysis.Should().BeFalse();

        // Switch to HumanVsComputer -> should become visible
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.ShowAnalysis.Should().BeTrue();

        // Toggle visibility off
        vm.ToggleAnalysisCommand.Execute(null);
        vm.IsAnalysisVisible.Should().BeFalse();
        vm.ShowAnalysis.Should().BeFalse();

        // Toggle visibility on
        vm.ToggleAnalysisCommand.Execute(null);
        vm.IsAnalysisVisible.Should().BeTrue();
        vm.ShowAnalysis.Should().BeTrue();

        // Switch to HumanVsHuman mode -> should hide even when IsAnalysisVisible is true
        vm.SetGameModeCommand.Execute(GameMode.HumanVsHuman);
        vm.ShowAnalysis.Should().BeFalse();
        vm.IsAnalysisVisible.Should().BeTrue();

        // Switch back to HumanVsComputer -> should be visible again
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.ShowAnalysis.Should().BeTrue();
    }

    [Fact]
    public void Analysis_UpdateAndReset_BehavesCorrectly()
    {
        var vm = new MainViewModel();

        // Initially placeholder values
        vm.Analysis.Move.Should().Be("-");
        vm.Analysis.Depth.Should().Be("-");
        vm.Analysis.Value.Should().Be("-");
        vm.Analysis.BestMove.Should().Be("-");
        vm.Analysis.Nodes.Should().Be("-");
        vm.Analysis.Evaluations.Should().Be("-");
        vm.Analysis.Time.Should().Be("-");

        // Update with metrics
        var analysis = new SearchAnalysis
        {
            Move = "32-28",
            Depth = "3 plies",
            Value = "+120",
            BestMove = "32-28",
            Nodes = "1,450",
            Evaluations = "820",
            Time = "0:00.1"
        };
        vm.Analysis.Update(analysis);

        vm.Analysis.Move.Should().Be("32-28");
        vm.Analysis.Depth.Should().Be("3 plies");
        vm.Analysis.Value.Should().Be("+120");
        vm.Analysis.BestMove.Should().Be("32-28");
        vm.Analysis.Nodes.Should().Be("1,450");
        vm.Analysis.Evaluations.Should().Be("820");
        vm.Analysis.Time.Should().Be("0:00.1");

        // New game resets analysis
        vm.NewGameCommand.Execute(null);

        vm.Analysis.Move.Should().Be("-");
        vm.Analysis.Depth.Should().Be("-");
        vm.Analysis.Value.Should().Be("-");
        vm.Analysis.BestMove.Should().Be("-");
        vm.Analysis.Nodes.Should().Be("-");
        vm.Analysis.Evaluations.Should().Be("-");
        vm.Analysis.Time.Should().Be("-");
    }

    [Fact]
    public async Task SaveCommand_WhenFileNameEmpty_PromptsForPathAndUpdatesTitle()
    {
        var files = new TestGameFileService { SavedPathResult = @"C:\Games\mygame.checkers" };
        var vm = new MainViewModel(fileService: files);

        vm.Title.Should().Be("Checkers (Draughts)");

        await vm.SaveCommand.ExecuteAsync(null);

        files.LastSavedCurrentPath.Should().BeNull();
        files.LastSavedText.Should().Contain("[GameMode \"HumanVsHuman\"]");
        vm.FileName.Should().Be(@"C:\Games\mygame.checkers");
        vm.Title.Should().Be("Checkers (Draughts) – mygame.checkers");
    }

    [Fact]
    public async Task SaveCommand_WhenFileNameAlreadySet_SavesDirectlyWithoutAsking()
    {
        var files = new TestGameFileService { SavedPathResult = @"C:\Games\existing.checkers" };
        var vm = new MainViewModel(fileService: files);

        // First save sets filename (currentPath was null)
        await vm.SaveCommand.ExecuteAsync(null);
        files.LastSavedCurrentPath.Should().BeNull();

        // Second save does not ask for name and passes existing path
        await vm.SaveCommand.ExecuteAsync(null);
        files.LastAskForName.Should().BeFalse();
        files.LastSavedCurrentPath.Should().Be(@"C:\Games\existing.checkers");
    }

    [Fact]
    public async Task SaveAsCommand_AlwaysPromptsForPath()
    {
        var files = new TestGameFileService { SavedPathResult = @"C:\Games\custom.checkers" };
        var vm = new MainViewModel(fileService: files);

        // Pre-set filename via first save
        await vm.SaveCommand.ExecuteAsync(null);

        // SaveAs must ask for name even though filename is set
        await vm.SaveAsCommand.ExecuteAsync(null);
        files.LastAskForName.Should().BeTrue();
        files.LastSavedCurrentPath.Should().Be(@"C:\Games\custom.checkers");
    }

    [Fact]
    public async Task OpenCommand_LoadsGameAndRestoresSettings()
    {
        // First make a move in a session to format
        var baseSession = new GameSession();
        var move1 = baseSession.LegalMoves.First();
        baseSession.TryMakeMove(move1);

        string savedContent = $"""
            [GameMode "HumanVsComputer"]
            [TimeControlMode "TimePerMove"]
            [SecondsPerMove "15"]

            {move1.Notation}
            """;

        var files = new TestGameFileService
        {
            FileToOpen = new GameFile(@"C:\SavedGames\match.checkers", savedContent)
        };
        var vm = new MainViewModel(fileService: files);

        await vm.OpenCommand.ExecuteAsync(null);

        vm.FileName.Should().Be(@"C:\SavedGames\match.checkers");
        vm.Title.Should().Be("Checkers (Draughts) – match.checkers");
        vm.GameMode.Should().Be(GameMode.HumanVsComputer);
        vm.Settings.Mode.Should().Be(TimeControlMode.TimePerMove);
        vm.Settings.SecondsPerMove.Should().Be(15);
        vm.Session.MoveHistory.Should().HaveCount(1);
        vm.Session.MoveHistory[0].Notation.Should().Be(move1.Notation);
        vm.MoveHistoryList.Should().HaveCount(1);
    }

    [Fact]
    public async Task OpenCommand_BackwardsCompatibilityWithDifficultyTag()
    {
        var baseSession = new GameSession();
        var move1 = baseSession.LegalMoves.First();

        string savedContent = $"""
            [GameMode "HumanVsComputer"]
            [Difficulty "Hard"]

            {move1.Notation}
            """;

        var files = new TestGameFileService
        {
            FileToOpen = new GameFile(@"C:\SavedGames\legacy.checkers", savedContent)
        };
        var vm = new MainViewModel(fileService: files);

        await vm.OpenCommand.ExecuteAsync(null);

        vm.GameMode.Should().Be(GameMode.HumanVsComputer);
        vm.Settings.Mode.Should().Be(TimeControlMode.FixedDepth);
        vm.Settings.Depth.Should().Be(8);
    }

    [Fact]
    public async Task OpenCommand_WhenCorruptFile_ShowsErrorDialog()
    {
        var dialogs = new TestDialogService();
        var files = new TestGameFileService
        {
            FileToOpen = new GameFile(@"C:\bad.checkers", "invalid-move-notation-here")
        };
        var vm = new MainViewModel(dialogService: dialogs, fileService: files);

        await vm.OpenCommand.ExecuteAsync(null);

        dialogs.Errors.Should().ContainSingle();
        dialogs.Errors[0].Should().Contain("The game could not be opened");
        vm.FileName.Should().BeEmpty();
    }

    [Fact]
    public void NewGameCommand_ResetsFileNameAndTitle()
    {
        var files = new TestGameFileService { SavedPathResult = @"C:\Games\sample.checkers" };
        var vm = new MainViewModel(fileService: files);

        vm.SaveCommand.Execute(null);
        vm.FileName.Should().Be(@"C:\Games\sample.checkers");
        vm.Title.Should().Contain("sample.checkers");

        vm.NewGameCommand.Execute(null);
        vm.FileName.Should().BeEmpty();
        vm.Title.Should().Be("Checkers (Draughts)");
    }

    private class TestAiPlayer : IPlayer
    {
        private readonly List<SearchAnalysis> _progressReports;
        private readonly Move _moveToReturn;

        public TestAiPlayer(Move moveToReturn, params SearchAnalysis[] progressReports)
        {
            _moveToReturn = moveToReturn;
            _progressReports = [.. progressReports];
        }

        public string Name => "Test AI";
        public SearchAnalysis? LastAnalysis { get; private set; }

        public ValueTask<Move> GetMoveAsync(BoardState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken = default) =>
            GetMoveAsync(state, legalMoves, progress: null, cancellationToken);

        public async ValueTask<Move> GetMoveAsync(BoardState state, IReadOnlyList<Move> legalMoves, IProgress<SearchAnalysis>? progress, CancellationToken cancellationToken = default)
        {
            foreach (var report in _progressReports)
            {
                LastAnalysis = report;
                progress?.Report(report);
                await Task.Yield();
            }
            return _moveToReturn;
        }
    }

    [Fact]
    public async Task AiTurn_UpdatesAnalysisPaneLiveViaProgress()
    {
        var vm = new MainViewModel
        {
            AiDelayMs = 0
        };
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);

        var intermediate = new SearchAnalysis
        {
            Move = "12-16",
            Depth = "2 plies",
            Value = "+45",
            BestMove = "12-16",
            Nodes = "320",
            Evaluations = "180",
            Time = "0:00.1"
        };
        var final = new SearchAnalysis
        {
            Move = "12-16",
            Depth = "4 plies",
            Value = "+120",
            BestMove = "12-16",
            Nodes = "1,850",
            Evaluations = "920",
            Time = "0:00.3"
        };

        var movePlayed = false;
        vm.AiPlayerFactory = (settings, time) =>
        {
            var legalMoves = vm.Session.LegalMoves;
            var chosen = legalMoves.First();
            return new TestAiPlayer(chosen, intermediate, final);
        };

        // Human plays (5,0) -> (4,1) to trigger AI turn
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);

        // Wait for AI turn to complete
        for (int i = 0; i < 30; i++)
        {
            if (vm.Session.MoveHistory.Count >= 2)
            {
                movePlayed = true;
                break;
            }
            await Task.Delay(25);
        }

        movePlayed.Should().BeTrue();
        vm.Analysis.Depth.Should().Be("4 plies");
        vm.Analysis.Value.Should().Be("+120");
        vm.Analysis.Nodes.Should().Be("1,850");
        vm.Analysis.Evaluations.Should().Be("920");
    }

    [Fact]
    public async Task AiTurn_WithDefaultMinimaxPlayer_UpdatesAnalysisPane()
    {
        var vm = new MainViewModel
        {
            AiDelayMs = 0,
            Settings = new GameSettings(TimeControlMode.FixedDepth, 2, 1, 1)
        };
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);

        // Human plays (5,0) -> (4,1) to trigger AI turn
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);

        for (int i = 0; i < 30; i++)
        {
            if (vm.Session.MoveHistory.Count >= 2)
                break;
            await Task.Delay(50);
        }

        vm.Session.MoveHistory.Should().HaveCountGreaterThanOrEqualTo(2);
        vm.Analysis.Move.Should().NotBe("-");
        vm.Analysis.Depth.Should().Contain("plies");
        vm.Analysis.Nodes.Should().NotBe("-");
    }

    [Fact]
    public void SettingsViewModel_VariantSelection_UpdatesVariantProperty()
    {
        var settings = GameSettings.Default; // International by default
        var vm = new SettingsViewModel(settings);

        vm.IsInternational.Should().BeTrue();
        vm.IsEnglish.Should().BeFalse();
        vm.Variant.Should().Be(CheckersVariant.International);

        // Switch to English
        vm.IsEnglish = true;
        vm.IsEnglish.Should().BeTrue();
        vm.IsInternational.Should().BeFalse();
        vm.Variant.Should().Be(CheckersVariant.English);

        var updated = vm.ToSettings();
        updated.Variant.Should().Be(CheckersVariant.English);
        updated.VariantName.Should().Be("English Checkers");
        updated.VariantBadgeText.Should().Be("English");
    }

    [Fact]
    public async Task EditSettings_VariantChanged_AtGameStart_SwitchesSessionVariantImmediately()
    {
        var dialogService = new TestDialogService
        {
            SettingsToReturn = new GameSettings(TimeControlMode.TimePerGame, 8, 5, 5, CheckersVariant.English)
        };

        var vm = new MainViewModel(dialogService: dialogService);
        vm.Session.RuleEngine.Variant.Should().Be(CheckersVariant.International);

        await vm.EditSettingsCommand.ExecuteAsync(null);

        vm.Settings.Variant.Should().Be(CheckersVariant.English);
        vm.Session.RuleEngine.Variant.Should().Be(CheckersVariant.English);
        vm.VariantBadgeText.Should().Be("English");
    }

    [Fact]
    public async Task EditSettings_VariantChanged_MidGame_PromptsConfirmation()
    {
        var dialogService = new TestDialogService
        {
            SettingsToReturn = new GameSettings(TimeControlMode.TimePerGame, 8, 5, 5, CheckersVariant.English),
            ConfirmResult = false // User cancels
        };

        var vm = new MainViewModel(dialogService: dialogService);
        // Play one move: (5,0) -> (4,1)
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);
        vm.Session.MoveHistory.Should().HaveCount(1);

        // Try to change variant, but decline confirmation
        await vm.EditSettingsCommand.ExecuteAsync(null);

        // Variant should remain International
        vm.Settings.Variant.Should().Be(CheckersVariant.International);
        vm.Session.RuleEngine.Variant.Should().Be(CheckersVariant.International);
        vm.Session.MoveHistory.Should().HaveCount(1);

        // Now user accepts confirmation
        dialogService.ConfirmResult = true;
        await vm.EditSettingsCommand.ExecuteAsync(null);

        // Game should restart with English variant
        vm.Settings.Variant.Should().Be(CheckersVariant.English);
        vm.Session.RuleEngine.Variant.Should().Be(CheckersVariant.English);
        vm.Session.MoveHistory.Should().BeEmpty();
    }

    [Fact]
    public void AnalysisViewModel_UpdatedEvent_FiresOnUpdateAndReset()
    {
        var vm = new AnalysisViewModel();
        int updatedCount = 0;
        vm.Updated += () => updatedCount++;

        vm.Update(new SearchAnalysis
        {
            Move = "12-16",
            Depth = "3 plies",
            Value = "+50",
            BestMove = "12-16",
            Nodes = "1,000",
            Evaluations = "500",
            Time = "0:00.1"
        });

        updatedCount.Should().Be(1);
        vm.Move.Should().Be("12-16");
        vm.Depth.Should().Be("3 plies");

        vm.Reset();

        updatedCount.Should().Be(2);
        vm.Move.Should().Be("-");
        vm.Depth.Should().Be("-");
    }

    [Fact]
    public void SettingsViewModel_TranspositionTablePower_UpdatesEntriesAndText()
    {
        var vm = new SettingsViewModel(GameSettings.Default);
        vm.TranspositionTablePower.Should().Be(20);
        vm.TranspositionTableEntries.Should().Be(1_048_576);
        vm.TranspositionTableEntriesText.Should().Be("1,048,576 entries");

        // Move slider to max (2^24 = 16,777,216)
        vm.TranspositionTablePower = 24;
        vm.TranspositionTableEntries.Should().Be(16_777_216);
        vm.TranspositionTableEntriesText.Should().Be("16,777,216 entries");

        var settings = vm.ToSettings();
        settings.TranspositionTableEntries.Should().Be(16_777_216);
    }

    [Fact]
    public async Task EditSettings_TranspositionTableSizeChanged_UpdatesAiPlayerTableCapacity()
    {
        var dialogService = new TestDialogService
        {
            SettingsToReturn = new GameSettings(
                TimeControlMode.TimePerGame,
                8,
                5,
                5,
                CheckersVariant.International,
                16_777_216)
        };

        var vm = new MainViewModel(dialogService: dialogService);
        var initialAi = (MinimaxPlayer)vm.CreateAiPlayer();
        initialAi.TranspositionTable!.Capacity.Should().Be(1_048_576);

        await vm.EditSettingsCommand.ExecuteAsync(null);

        vm.Settings.TranspositionTableEntries.Should().Be(16_777_216);
        var updatedAi = (MinimaxPlayer)vm.CreateAiPlayer();
        updatedAi.TranspositionTable!.Capacity.Should().Be(16_777_216);
    }

    [Fact]
    public void SetGameMode_ComputerVsHuman_FlipsBoardAndSetsPlayerLabels()
    {
        var vm = new MainViewModel();

        // Initially un-flipped
        vm.IsBoardFlipped.Should().BeFalse();
        vm.BoardSquares[0].Position.Should().Be(new Position(0, 0));

        // Switch to ComputerVsHuman
        vm.SetGameModeCommand.Execute(GameMode.ComputerVsHuman);

        vm.GameMode.Should().Be(GameMode.ComputerVsHuman);
        vm.IsComputerVsHuman.Should().BeTrue();
        vm.IsBoardFlipped.Should().BeTrue();
        vm.WhitePlayerLabel.Should().Be("Computer");
        vm.BlackPlayerLabel.Should().Be("Human");

        // BoardSquares should now be ordered in flipped orientation (sr=0, sc=0 -> r=7, c=7)
        vm.BoardSquares[0].Position.Should().Be(new Position(7, 7));
        vm.BoardSquares[63].Position.Should().Be(new Position(0, 0));
    }

    [Fact]
    public async Task SetGameMode_ComputerVsHuman_TriggersAiOpeningMoveForWhite()
    {
        var vm = new MainViewModel
        {
            AiDelayMs = 0
        };

        var openingMove = vm.Session.LegalMoves.First();
        vm.AiPlayerFactory = (settings, time) => new TestAiPlayer(openingMove);

        // When switching to ComputerVsHuman at start of game (move 1, White to play),
        // AI should immediately execute White's move.
        vm.SetGameModeCommand.Execute(GameMode.ComputerVsHuman);

        for (int i = 0; i < 30; i++)
        {
            if (vm.Session.MoveHistory.Count >= 1)
                break;
            await Task.Delay(25);
        }

        vm.Session.MoveHistory.Should().HaveCount(1);
        vm.Session.MoveHistory[0].Notation.Should().Be(openingMove.Notation);
        vm.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);
    }

    [Fact]
    public async Task Undo_InComputerVsHuman_UndosToHumanTurn()
    {
        var vm = new MainViewModel
        {
            AiDelayMs = 0
        };

        var whiteMove1 = vm.Session.LegalMoves.First();
        vm.AiPlayerFactory = (settings, time) =>
        {
            var moves = vm.Session.LegalMoves;
            return new TestAiPlayer(moves.First());
        };

        // 1. Switch to ComputerVsHuman: AI makes move 1 (White)
        vm.SetGameModeCommand.Execute(GameMode.ComputerVsHuman);
        for (int i = 0; i < 30; i++)
        {
            if (vm.Session.MoveHistory.Count >= 1) break;
            await Task.Delay(25);
        }
        vm.Session.MoveHistory.Should().HaveCount(1);
        vm.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);

        // 2. Human (Black) makes move 2
        var blackMove = vm.Session.LegalMoves.First();
        vm.ExecutePlayerMove(blackMove);
        vm.Session.MoveHistory.Should().HaveCount(2);

        // 3. AI makes move 3 (White)
        for (int i = 0; i < 30; i++)
        {
            if (vm.Session.MoveHistory.Count >= 3) break;
            await Task.Delay(25);
        }
        vm.Session.MoveHistory.Should().HaveCount(3);
        vm.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);

        // 4. Human clicks Undo -> both move 3 (White AI) and move 2 (Black Human) should be undone,
        // returning to Black Human's turn at ply 1.
        vm.UndoCommand.Execute(null);

        vm.Session.MoveHistory.Should().HaveCount(1);
        vm.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);
    }

    [Fact]
    public void Constructor_WithSettingsStore_LoadsPreferences()
    {
        var store = new TestSettingsStore
        {
            Preferences = new AppPreferences(
                GameSettings.Default with { Variant = CheckersVariant.International, SecondsPerMove = 5 },
                ShowAnalysis: false,
                SoundEnabled: false)
        };

        var vm = new MainViewModel(settingsStore: store);

        vm.Settings.Variant.Should().Be(CheckersVariant.International);
        vm.IsAnalysisVisible.Should().BeFalse();
        vm.IsSoundOn.Should().BeFalse();
    }

    [Fact]
    public void ToggleSound_And_ToggleAnalysis_AutoSavesToSettingsStore()
    {
        var store = new TestSettingsStore();
        var vm = new MainViewModel(settingsStore: store);

        store.SavedHistory.Should().BeEmpty();

        vm.ToggleSoundCommand.Execute(null);
        store.SavedHistory.Should().HaveCount(1);
        store.Preferences.SoundEnabled.Should().BeFalse();

        vm.ToggleAnalysisCommand.Execute(null);
        store.SavedHistory.Should().HaveCount(2);
        store.Preferences.ShowAnalysis.Should().BeFalse();
    }

    [Fact]
    public async Task MoveNowCommand_CanExecute_OnlyWhenAiThinking_AndCancelsAiEarly()
    {
        var tcs = new TaskCompletionSource<Move>();
        var fakeEngineHost = new FakeEngineHost(tcs);

        var vm = new MainViewModel(engineHost: fakeEngineHost)
        {
            AiDelayMs = 0
        };

        vm.MoveNowCommand.CanExecute(null).Should().BeFalse();

        vm.SetGameModeCommand.Execute(GameMode.ComputerVsHuman);

        for (int i = 0; i < 20 && !vm.IsAiThinking; i++)
        {
            await Task.Delay(25);
        }

        vm.IsAiThinking.Should().BeTrue();
        vm.MoveNowCommand.CanExecute(null).Should().BeTrue();

        fakeEngineHost.MoveNowTokenReceived.IsCancellationRequested.Should().BeFalse();

        vm.MoveNowCommand.Execute(null);

        fakeEngineHost.MoveNowTokenReceived.IsCancellationRequested.Should().BeTrue();

        tcs.SetResult(vm.Session.LegalMoves.First());

        for (int i = 0; i < 20 && vm.IsAiThinking; i++)
        {
            await Task.Delay(25);
        }

        vm.IsAiThinking.Should().BeFalse();
        vm.MoveNowCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void NewGame_And_VariantChange_ClearsEngineHostTranspositionTable()
    {
        var fakeEngineHost = new FakeEngineHost(new TaskCompletionSource<Move>());
        var vm = new MainViewModel(engineHost: fakeEngineHost);

        fakeEngineHost.ClearTranspositionTableCount.Should().Be(0);

        vm.NewGameCommand.Execute(null);
        fakeEngineHost.ClearTranspositionTableCount.Should().Be(1);

        vm.Settings = vm.Settings with { Variant = CheckersVariant.English };
        fakeEngineHost.ClearTranspositionTableCount.Should().Be(2);
    }

    [Theory]
    [InlineData(GameMode.HumanVsComputer, true)]
    [InlineData(GameMode.ComputerVsHuman, true)]
    [InlineData(GameMode.ComputerVsComputer, true)]
    [InlineData(GameMode.HumanVsHuman, false)]
    public void IsVsComputer_ReturnsTrue_WhenPlayingComputer(GameMode mode, bool expected)
    {
        var vm = new MainViewModel();
        vm.SetGameMode(mode);
        vm.IsVsComputer.Should().Be(expected);
    }

    [Fact]
    public void SwitchSides_EnabledOnlyWhenPlayingComputerAndHumanTurn()
    {
        var vm = new MainViewModel();

        // 1. In HumanVsHuman: disabled
        vm.CanSwitchSides.Should().BeFalse();
        vm.SwitchSidesCommand.CanExecute(null).Should().BeFalse();

        // 2. In HumanVsComputer at start (White's turn, Human is White): enabled
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.CanSwitchSides.Should().BeTrue();
        vm.SwitchSidesCommand.CanExecute(null).Should().BeTrue();

        // 3. Switch sides -> becomes ComputerVsHuman, board is flipped, White is now computer
        vm.SwitchSidesCommand.Execute(null);
        vm.GameMode.Should().Be(GameMode.ComputerVsHuman);
        vm.IsBoardFlipped.Should().BeTrue();
    }

    [Fact]
    public async Task SwitchSides_TriggersAiMove_WhenSwitchedToComputerTurn()
    {
        var vm = new MainViewModel();
        var openingMove = vm.Session.LegalMoves.First();
        vm.AiPlayerFactory = (settings, time) => new TestAiPlayer(openingMove);

        // Start in HumanVsComputer (Human is White)
        vm.SetGameModeCommand.Execute(GameMode.HumanVsComputer);
        vm.Session.MoveHistory.Should().BeEmpty();

        // Human switches sides -> Computer is now White and immediately plays
        vm.SwitchSidesCommand.Execute(null);

        for (int i = 0; i < 30; i++)
        {
            if (vm.Session.MoveHistory.Count >= 1)
                break;
            await Task.Delay(25);
        }

        vm.Session.MoveHistory.Should().HaveCount(1);
        vm.Session.MoveHistory[0].Notation.Should().Be(openingMove.Notation);
        vm.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.Black);
        vm.IsHumanVsComputer.Should().BeFalse();
        vm.IsComputerVsHuman.Should().BeTrue();
    }

    [Fact]
    public void Clocks_StartAtInitialTime_AndDoNotTickBeforeFirstMove()
    {
        var vm = new MainViewModel();
        vm.Settings = new GameSettings(TimeControlMode.TimePerGame, 8, 5, 5); // 5 min
        vm.ResetClock();

        vm.WhiteClockText.Should().Be("5:00");
        vm.BlackClockText.Should().Be("5:00");
        vm.WhiteTimeLeft.Should().Be(TimeSpan.FromMinutes(5));
        vm.BlackTimeLeft.Should().Be(TimeSpan.FromMinutes(5));

        Thread.Sleep(300);
        vm.WhiteClockText.Should().Be("5:00");
        vm.BlackClockText.Should().Be("5:00");
    }

    [Fact]
    public void FlagFell_EndsGameWithTimeoutLoss()
    {
        var vm = new MainViewModel();
        _ = vm.SetGameMode(GameMode.HumanVsComputer);
        vm.Session.Status.Should().Be(GameStatus.InProgress);

        vm.Session.FlagFell(PieceColor.White);

        vm.Session.Status.Should().Be(GameStatus.BlackWon);
        vm.Session.GameOverReason.Should().Be(GameOverReason.Timeout);
        vm.StatusText.Should().Contain("Black Won on time");
    }

    [Fact]
    public async Task SetGameMode_VsComputer_InMiddleOfGame_PromptsConfirmation_AndAbortsIfCanceled()
    {
        var dialogs = new TestDialogService { ConfirmResult = false };
        var vm = new MainViewModel(dialogService: dialogs);

        // Start game and make a move
        await vm.SetGameMode(GameMode.HumanVsComputer);
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);
        vm.Session.MoveHistory.Should().HaveCount(1);
        dialogs.ConfirmCallCount.Should().Be(0);

        // Player clicks "vs Computer (White)" in the middle of the game
        await vm.SetGameMode(GameMode.ComputerVsHuman);

        // Warning dialog was shown with option to cancel
        dialogs.ConfirmCallCount.Should().Be(1);
        dialogs.LastConfirmTitle.Should().Be("Start New Game");
        dialogs.LastConfirmMessage.Should().Contain("game is currently in progress");

        // Since user canceled (ConfirmResult = false), game is still in progress with move history intact
        vm.Session.MoveHistory.Should().HaveCount(1);
        vm.GameMode.Should().Be(GameMode.HumanVsComputer);
    }

    [Fact]
    public async Task SetGameMode_VsComputer_InMiddleOfGame_StartsNewGameIfConfirmed()
    {
        var dialogs = new TestDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogService: dialogs);

        // Start game and make a move
        await vm.SetGameMode(GameMode.HumanVsComputer);
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);
        vm.Session.MoveHistory.Should().HaveCount(1);

        // Player clicks "vs Computer (White)" in middle of game and confirms
        await vm.SetGameMode(GameMode.ComputerVsHuman);

        dialogs.ConfirmCallCount.Should().Be(1);
        vm.GameMode.Should().Be(GameMode.ComputerVsHuman);
        // New game was started, history cleared
        vm.Session.MoveHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task SetGameMode_VsComputer_WhenAlreadyInSameMode_StartsNewGame()
    {
        var dialogs = new TestDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogService: dialogs);

        // Start game in HumanVsComputer and make a move
        await vm.SetGameMode(GameMode.HumanVsComputer);
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);
        vm.Session.MoveHistory.Should().HaveCount(1);

        // Player clicks "vs Computer (Black)" again to start new game
        await vm.SetGameMode(GameMode.HumanVsComputer);

        dialogs.ConfirmCallCount.Should().Be(1);
        vm.GameMode.Should().Be(GameMode.HumanVsComputer);
        vm.Session.MoveHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task SetGameMode_VsComputer_AtStartOfGame_StartsNewGameWithoutPrompt()
    {
        var dialogs = new TestDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogService: dialogs);

        // At move 0 (not in the middle of a game)
        vm.Session.MoveHistory.Should().BeEmpty();

        await vm.SetGameMode(GameMode.ComputerVsHuman);

        dialogs.ConfirmCallCount.Should().Be(0);
        vm.GameMode.Should().Be(GameMode.ComputerVsHuman);
        vm.Session.MoveHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task GameModified_Undo_MarksModifiedWhenPlayingComputer()
    {
        var vm = new MainViewModel();
        await vm.SetGameMode(GameMode.HumanVsComputer);

        vm.IsGameModified.Should().BeFalse();
        vm.IsStatusModified.Should().BeFalse();

        // Make move
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);
        vm.Session.MoveHistory.Should().HaveCount(1);

        // Undo move
        vm.UndoCommand.Execute(null);

        vm.IsGameModified.Should().BeTrue();
        vm.IsStatusModified.Should().BeTrue();

        // New game resets modified
        vm.NewGameCommand.Execute(null);
        vm.IsGameModified.Should().BeFalse();
        vm.IsStatusModified.Should().BeFalse();
    }

    [Fact]
    public async Task GameModified_SwitchSides_MarksModified()
    {
        var vm = new MainViewModel();
        await vm.SetGameMode(GameMode.HumanVsComputer);

        vm.IsGameModified.Should().BeFalse();
        vm.IsStatusModified.Should().BeFalse();

        vm.SwitchSidesCommand.Execute(null);

        vm.IsGameModified.Should().BeTrue();
        vm.IsStatusModified.Should().BeTrue();
    }

    [Fact]
    public async Task GameModified_SettingsMidGame_MarksModified()
    {
        var dialogs = new TestDialogService
        {
            SettingsToReturn = new GameSettings(TimeControlMode.FixedDepth, 4, 1, 1)
        };
        var vm = new MainViewModel(dialogService: dialogs);
        await vm.SetGameMode(GameMode.HumanVsComputer);

        // Play move
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);

        vm.IsGameModified.Should().BeFalse();

        // Edit settings mid game
        await vm.EditSettingsCommand.ExecuteAsync(null);

        vm.IsGameModified.Should().BeTrue();
        vm.IsStatusModified.Should().BeTrue();
    }

    [Fact]
    public void GameModified_TwoPlayers_DoesNotShowStatusModified()
    {
        var vm = new MainViewModel();
        // Default mode is HumanVsHuman
        vm.SquareClickedCommand.Execute(vm.Squares[5, 0]);
        vm.SquareClickedCommand.Execute(vm.Squares[4, 1]);

        vm.UndoCommand.Execute(null);

        vm.IsGameModified.Should().BeFalse();
        vm.IsStatusModified.Should().BeFalse();
    }

    [Fact]
    public async Task HandleTimeout_WhenHumanTimesOutVsComputer_AndUserSaysYes_AllowsGameToContinue_KeepsTimeAtZero_AndMarksModified()
    {
        var dialogs = new TestDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogService: dialogs);
        await vm.SetGameMode(GameMode.HumanVsComputer);
        vm.Settings = new GameSettings(TimeControlMode.TimePerGame, 8, 5, 5);
        vm.ResetClock();

        // White (Human) times out
        await vm.HandleTimeoutAsync(PieceColor.White);

        dialogs.ConfirmCallCount.Should().Be(1);
        dialogs.LastConfirmTitle.Should().Be("Time Expired");
        dialogs.LastConfirmMessage.Should().Contain("continue playing");

        // Game continues
        vm.Session.Status.Should().Be(GameStatus.InProgress);
        vm.WhiteTimeLeft.Should().Be(TimeSpan.Zero);
        vm.WhiteClockText.Should().Be("0:00");
        vm.IsGameModified.Should().BeTrue();
        vm.IsStatusModified.Should().BeTrue();
    }

    [Fact]
    public async Task HandleTimeout_WhenHumanTimesOutVsComputer_AndUserSaysNo_EndsGameOnTime()
    {
        var dialogs = new TestDialogService { ConfirmResult = false };
        var vm = new MainViewModel(dialogService: dialogs);
        await vm.SetGameMode(GameMode.HumanVsComputer);

        // White (Human) times out and rejects continuation
        await vm.HandleTimeoutAsync(PieceColor.White);

        dialogs.ConfirmCallCount.Should().Be(1);
        vm.Session.Status.Should().Be(GameStatus.BlackWon);
        vm.Session.GameOverReason.Should().Be(GameOverReason.Timeout);
        vm.StatusText.Should().Contain("Black Won on time");
    }

    [Fact]
    public async Task HandleTimeout_WhenComputerTimesOut_DoesNotPrompt_AndEndsGameImmediately()
    {
        var dialogs = new TestDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogService: dialogs);
        await vm.SetGameMode(GameMode.HumanVsComputer);

        // Black (Computer) times out
        await vm.HandleTimeoutAsync(PieceColor.Black);

        dialogs.ConfirmCallCount.Should().Be(0);
        vm.Session.Status.Should().Be(GameStatus.WhiteWon);
        vm.Session.GameOverReason.Should().Be(GameOverReason.Timeout);
        vm.StatusText.Should().Contain("White Won on time");
    }

    [Fact]
    public async Task HandleTimeout_WhenHumanInComputerVsHumanTimesOut_AndUserSaysYes_AllowsGameToContinue()
    {
        var dialogs = new TestDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogService: dialogs);
        await vm.SetGameMode(GameMode.ComputerVsHuman);

        // Black (Human) times out
        await vm.HandleTimeoutAsync(PieceColor.Black);

        dialogs.ConfirmCallCount.Should().Be(1);
        vm.Session.Status.Should().Be(GameStatus.InProgress);
        vm.BlackTimeLeft.Should().Be(TimeSpan.Zero);
        vm.BlackClockText.Should().Be("0:00");
        vm.IsGameModified.Should().BeTrue();
        vm.IsStatusModified.Should().BeTrue();
    }

    [Fact]
    public async Task HandleTimeout_InTwoPlayerMode_DoesNotPrompt_AndEndsGameImmediately()
    {
        var dialogs = new TestDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogService: dialogs);
        // Default mode is HumanVsHuman

        await vm.HandleTimeoutAsync(PieceColor.White);

        dialogs.ConfirmCallCount.Should().Be(0);
        vm.Session.Status.Should().Be(GameStatus.BlackWon);
        vm.Session.GameOverReason.Should().Be(GameOverReason.Timeout);
    }

    private class TestSettingsStore : ISettingsStore
    {
        public AppPreferences Preferences { get; set; } = new(GameSettings.Default, ShowAnalysis: true, SoundEnabled: true);
        public List<AppPreferences> SavedHistory { get; } = [];

        public AppPreferences Load() => Preferences;
        public bool Save(AppPreferences preferences)
        {
            Preferences = preferences;
            SavedHistory.Add(preferences);
            return true;
        }
    }

    private class FakeEngineHost : IEngineHost
    {
        private readonly TaskCompletionSource<Move> _tcs;
        public CancellationToken MoveNowTokenReceived { get; private set; }

        public FakeEngineHost(TaskCompletionSource<Move> tcs) => _tcs = tcs;
        public int ClearTranspositionTableCount { get; private set; }
        public void ClearTranspositionTable() => ClearTranspositionTableCount++;

        public async Task<(Move Move, SearchAnalysis? LastAnalysis)> ChooseMoveAsync(
            BoardState state,
            IReadOnlyList<Move> legalMoves,
            GameSettings settings,
            TimeSpan computerTimeLeft,
            IReadOnlyList<ulong> stateHashHistory,
            IProgress<SearchAnalysis>? progress,
            CancellationToken cancellationToken,
            CancellationToken moveNowToken)
        {
            MoveNowTokenReceived = moveNowToken;
            var move = await _tcs.Task;
            return (move, null);
        }
    }
}
