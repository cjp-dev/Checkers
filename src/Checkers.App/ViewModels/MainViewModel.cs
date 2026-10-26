using System.Collections.ObjectModel;
using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.Core.AI;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace Checkers.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISoundService _soundService;
    private readonly IDialogService? _dialogService;
    private readonly IGameFileService? _fileService;
    private readonly LocalEngineHost _localEngineHost = new();
    private readonly IEngineHost _engineHost;
    private readonly ISettingsStore? _settingsStore;
    private readonly bool _settingsLoaded;
    private Position? _selectedPosition;
    private CancellationTokenSource? _aiCts;
    private CancellationTokenSource? _moveNowCts;
    private int _searchId;

    public IEngineHost EngineHost => _engineHost;

    /// <summary>
    /// Artificial delay in milliseconds before AI starts searching to provide a natural feel.
    /// Can be set to 0 in automated unit tests.
    /// </summary>
    public int AiDelayMs { get; set; } = 250;

    public GameSession Session { get; private set; }
    public SquareViewModel[,] Squares { get; } = new SquareViewModel[8, 8];
    public ObservableCollection<SquareViewModel> BoardSquares { get; } = [];
    public ObservableCollection<Move> MoveHistoryList { get; } = [];

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _turnIndicatorText = string.Empty;

    [ObservableProperty]
    private string _moveCountText = string.Empty;

    [ObservableProperty]
    private int _whitePiecesCount;

    [ObservableProperty]
    private int _blackPiecesCount;

    [ObservableProperty]
    private int _whiteKingsCount;

    [ObservableProperty]
    private int _blackKingsCount;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private bool _isSoundOn = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwitchSidesCommand))]
    private bool _isAiThinking;

    [RelayCommand(CanExecute = nameof(IsAiThinking))]
    public void MoveNow() => _moveNowCts?.Cancel();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchSidesCommand))]
    private GameMode _gameMode = GameMode.HumanVsHuman;

    [ObservableProperty]
    private bool _isGameModified;

    private bool _hasHumanContinuedOnTime;
    private bool _isTimeoutPrompting;

    public bool IsStatusModified => IsVsComputer && IsGameModified;

    partial void OnIsGameModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStatusModified));
    }

    public bool CanSwitchSides =>
        (GameMode is GameMode.HumanVsComputer or GameMode.ComputerVsHuman) &&
        IsHumanTurn() &&
        Session.Status == GameStatus.InProgress &&
        !IsAiThinking;

    [RelayCommand(CanExecute = nameof(CanSwitchSides))]
    public void SwitchSides()
    {
        if (!CanSwitchSides)
            return;

        CancelAi();
        ClearSelection();

        GameMode = GameMode == GameMode.HumanVsComputer
            ? GameMode.ComputerVsHuman
            : GameMode.HumanVsComputer;

        IsGameModified = true;

        if (IsHumanVsComputerPlayer(PieceColor.White) && _whiteTimeLeft <= TimeSpan.Zero)
            _hasHumanContinuedOnTime = true;
        else if (IsHumanVsComputerPlayer(PieceColor.Black) && _blackTimeLeft <= TimeSpan.Zero)
            _hasHumanContinuedOnTime = true;
        else
            _hasHumanContinuedOnTime = false;

        RefreshBoard();
    }

    [ObservableProperty]
    private GameSettings _settings = GameSettings.Default;

    private TimeSpan _whiteTimeLeft;
    private TimeSpan _blackTimeLeft;
    private readonly Dictionary<int, (TimeSpan White, TimeSpan Black)> _timeLeftAtPly = [];
    private long _lastTickTimestamp;
    private readonly Timer _clockTimer;
    private readonly SynchronizationContext? _syncContext;

    public bool IsHumanVsComputer => GameMode == GameMode.HumanVsComputer;
    public bool IsComputerVsHuman => GameMode == GameMode.ComputerVsHuman;
    public bool IsVsComputer => GameMode is GameMode.HumanVsComputer or GameMode.ComputerVsHuman or GameMode.ComputerVsComputer;
    public bool IsHumanVsHuman => GameMode == GameMode.HumanVsHuman;
    public bool IsBoardFlipped => GameMode == GameMode.ComputerVsHuman;

    public string SettingsBadgeText => Settings.Summary;
    public string VariantBadgeText => Settings.VariantBadgeText;

    public bool IsGameSettingVisible => IsVsComputer;
    public bool IsClockVisible => Settings.Mode == TimeControlMode.TimePerGame;

    public TimeSpan WhiteTimeLeft => _whiteTimeLeft;
    public TimeSpan BlackTimeLeft => _blackTimeLeft;

    public string WhiteClockText => FormatClock(_whiteTimeLeft);
    public string BlackClockText => FormatClock(_blackTimeLeft);

    public TimeSpan ComputerTimeLeft => GameMode switch
    {
        GameMode.ComputerVsHuman => _whiteTimeLeft,
        GameMode.HumanVsComputer => _blackTimeLeft,
        GameMode.ComputerVsComputer => (Session.CurrentState.ActivePlayer == PieceColor.White ? _whiteTimeLeft : _blackTimeLeft),
        _ => TimeSpan.Zero
    };

    public string ClockText => IsClockVisible
        ? $"Clock: {FormatClock(ComputerTimeLeft)}"
        : string.Empty;

    private static string FormatClock(TimeSpan span) => span <= TimeSpan.Zero ? "0:00" : $"{(int)span.TotalMinutes}:{span.Seconds:00}";

    public string GameModeBadgeText => GameMode switch
    {
        GameMode.HumanVsComputer => "vs Computer",
        GameMode.ComputerVsHuman => "vs Computer",
        GameMode.HumanVsHuman => "vs Human",
        GameMode.ComputerVsComputer => "Computer vs Computer",
        _ => "Checkers"
    };

    public string GameModeDescription => GameMode switch
    {
        GameMode.HumanVsComputer => $"vs Computer ({Settings.Summary})",
        GameMode.ComputerVsHuman => $"vs Computer ({Settings.Summary})",
        GameMode.HumanVsHuman => "Human vs Human",
        GameMode.ComputerVsComputer => "Computer vs Computer",
        _ => string.Empty
    };

    public string WhitePlayerLabel => GameMode switch
    {
        GameMode.HumanVsComputer => "Human",
        GameMode.ComputerVsHuman => "Computer",
        GameMode.HumanVsHuman => "Player 1",
        GameMode.ComputerVsComputer => "Computer 1",
        _ => "White"
    };

    public string BlackPlayerLabel => GameMode switch
    {
        GameMode.HumanVsComputer => "Computer",
        GameMode.ComputerVsHuman => "Human",
        GameMode.HumanVsHuman => "Player 2",
        GameMode.ComputerVsComputer => "Computer 2",
        _ => "Black"
    };

    public AnalysisViewModel Analysis { get; } = new();

    [ObservableProperty]
    private bool _isAnalysisVisible = true;

    public bool ShowAnalysis => IsAnalysisVisible && IsVsComputer;

    [RelayCommand]
    public void ToggleAnalysis()
    {
        IsAnalysisVisible = !IsAnalysisVisible;
    }

    partial void OnGameModeChanged(GameMode value)
    {
        UpdateBoardSquaresOrder();
        OnPropertyChanged(nameof(IsHumanVsComputer));
        OnPropertyChanged(nameof(IsComputerVsHuman));
        OnPropertyChanged(nameof(IsVsComputer));
        OnPropertyChanged(nameof(IsHumanVsHuman));
        OnPropertyChanged(nameof(IsBoardFlipped));
        OnPropertyChanged(nameof(ShowAnalysis));
        OnPropertyChanged(nameof(GameModeBadgeText));
        OnPropertyChanged(nameof(GameModeDescription));
        OnPropertyChanged(nameof(WhitePlayerLabel));
        OnPropertyChanged(nameof(BlackPlayerLabel));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(IsClockVisible));
        OnPropertyChanged(nameof(IsGameSettingVisible));
        OnPropertyChanged(nameof(IsStatusModified));
    }

    partial void OnIsAnalysisVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAnalysis));
        SavePreferences();
    }

    partial void OnIsSoundOnChanged(bool value)
    {
        _soundService.IsEnabled = value;
        SavePreferences();
    }

    partial void OnSettingsChanging(GameSettings? oldValue, GameSettings newValue)
    {
        if (oldValue != null && oldValue.Variant != newValue.Variant)
        {
            _engineHost.ClearTranspositionTable();
        }
    }

    partial void OnSettingsChanged(GameSettings value)
    {
        OnPropertyChanged(nameof(SettingsBadgeText));
        OnPropertyChanged(nameof(VariantBadgeText));
        OnPropertyChanged(nameof(GameModeDescription));
        OnPropertyChanged(nameof(WhitePlayerLabel));
        OnPropertyChanged(nameof(BlackPlayerLabel));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(IsClockVisible));
        OnPropertyChanged(nameof(IsGameSettingVisible));
        OnPropertyChanged(nameof(IsStatusModified));
        OnPropertyChanged(nameof(WhiteClockText));
        OnPropertyChanged(nameof(BlackClockText));
        SavePreferences();
    }

    private void SavePreferences()
    {
        if (_settingsLoaded)
        {
            _settingsStore?.Save(new AppPreferences(Settings, IsAnalysisVisible, IsSoundOn));
        }
    }

    [ObservableProperty]
    private string _fileName = string.Empty;

    public string Title => string.IsNullOrEmpty(FileName)
        ? "Checkers (Draughts)"
        : $"Checkers (Draughts) – {System.IO.Path.GetFileName(FileName)}";

    partial void OnFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(Title));
    }

    public MainViewModel(
        GameSession? session = null,
        ISoundService? soundService = null,
        IDialogService? dialogService = null,
        IGameFileService? fileService = null,
        IEngineHost? engineHost = null,
        ISettingsStore? settingsStore = null)
    {
        _soundService = soundService ?? new DummySoundService();
        _dialogService = dialogService;
        _fileService = fileService;
        _engineHost = engineHost ?? _localEngineHost;
        _settingsStore = settingsStore;

        if (_settingsStore != null)
        {
            var saved = _settingsStore.Load();
            _settings = saved.Game.Normalize();
            _isAnalysisVisible = saved.ShowAnalysis;
            _isSoundOn = saved.SoundEnabled;
            _soundService.IsEnabled = saved.SoundEnabled;
        }
        _settingsLoaded = true;

        Session = session ?? new GameSession(new RuleEngine(Settings.Variant));
        _whiteTimeLeft = Settings.GameTime;
        _blackTimeLeft = Settings.GameTime;
        _syncContext = SynchronizationContext.Current;
        _clockTimer = new Timer(OnClockTimerTick, null, 250, 250);

        // Initialize 8x8 squares
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var sq = new SquareViewModel(r, c);
                Squares[r, c] = sq;
            }
        }
        UpdateBoardSquaresOrder();

        Session.MoveExecuted += OnMoveExecutedHandler;
        Session.GameOver += OnGameOverHandler;

        RefreshBoard();
    }

    private void UpdateBoardSquaresOrder()
    {
        BoardSquares.Clear();
        for (int sr = 0; sr < 8; sr++)
        {
            for (int sc = 0; sc < 8; sc++)
            {
                int r = IsBoardFlipped ? 7 - sr : sr;
                int c = IsBoardFlipped ? 7 - sc : sc;
                BoardSquares.Add(Squares[r, c]);
            }
        }
    }

    public void ResetClock()
    {
        _whiteTimeLeft = Settings.GameTime;
        _blackTimeLeft = Settings.GameTime;
        _timeLeftAtPly.Clear();
        _lastTickTimestamp = 0;
        _hasHumanContinuedOnTime = false;
        _isTimeoutPrompting = false;
        OnPropertyChanged(nameof(WhiteTimeLeft));
        OnPropertyChanged(nameof(BlackTimeLeft));
        OnPropertyChanged(nameof(WhiteClockText));
        OnPropertyChanged(nameof(BlackClockText));
        OnPropertyChanged(nameof(ClockText));
    }

    private void AttachSession(GameSession newSession)
    {
        Session.MoveExecuted -= OnMoveExecutedHandler;
        Session.GameOver -= OnGameOverHandler;

        Session = newSession;
        IsGameModified = false;

        Session.MoveExecuted += OnMoveExecutedHandler;
        Session.GameOver += OnGameOverHandler;
    }

    private void OnMoveExecutedHandler(object? sender, MoveExecutedEventArgs e) => OnMoveExecuted();
    private void OnGameOverHandler(object? sender, GameOverEventArgs e) => HandleGameOver(e);

    [RelayCommand]
    public void SquareClicked(SquareViewModel? square)
    {
        if (IsAiThinking || square == null || Session.Status != GameStatus.InProgress)
            return;

        // 1. Clicked on a valid target square for the currently selected piece
        if (square.IsValidTarget && _selectedPosition.HasValue)
        {
            var matchingMove = Session.LegalMoves.FirstOrDefault(m =>
                m.From == _selectedPosition.Value && m.To == square.Position);

            if (matchingMove != null)
            {
                ExecutePlayerMove(matchingMove);
                return;
            }
        }

        // 2. Clicked on the currently selected piece -> Deselect
        if (_selectedPosition.HasValue && square.Position == _selectedPosition.Value)
        {
            ClearSelection();
            UpdateStatusText();
                SwitchSidesCommand.NotifyCanExecuteChanged();
            return;
        }

        // 3. Clicked on a friendly piece (only if human player controls current turn)
        if (IsHumanTurn() && square.HasPiece && square.Piece!.Value.Color == Session.CurrentState.ActivePlayer)
        {
            var movesForPiece = Session.LegalMoves.Where(m => m.From == square.Position).ToList();
            if (movesForPiece.Count > 0)
            {
                SelectPiece(square.Position, movesForPiece);
                return;
            }
            else
            {
                // Friendly piece has no legal moves (blocked or forced capture elsewhere)
                var sqNum = square.DraughtsNumber.HasValue ? $"#{square.DraughtsNumber.Value}" : $"({square.Row},{square.Col})";
                if (Session.LegalMoves.Any(m => m.IsCapture))
                {
                    StatusText = $"{TurnIndicatorText} — Mandatory jump! Select a red-outlined piece.";
                }
                else
                {
                    StatusText = $"Piece on square {sqNum} has no legal moves (blocked). Select a piece that can move.";
                }
                ClearSelection();
                return;
            }
        }

        // 4. Clicked on opponent piece
        if (square.HasPiece && square.Piece!.Value.Color != Session.CurrentState.ActivePlayer)
        {
            var opponent = square.Piece!.Value.Color == PieceColor.White ? "White" : "Black";
            StatusText = $"That is a {opponent} piece. It is {TurnIndicatorText}!";
            ClearSelection();
            return;
        }

        // 5. Clicked on empty non-target square -> Deselect
        ClearSelection();
        UpdateStatusText();
        SwitchSidesCommand.NotifyCanExecuteChanged();
    }

    public bool ExecutePlayerMove(Move move)
    {
        bool success = Session.TryMakeMove(move);
        if (success)
        {
            if (move.IsPromotion)
                _soundService.Play(SoundType.King);
            else if (move.IsCapture)
                _soundService.Play(SoundType.Capture);
            else
                _soundService.Play(SoundType.Move);

            ClearSelection();
            HighlightLastMove(move);
            return true;
        }
        return false;
    }

    public bool TryDragMove(SquareViewModel fromSquare, SquareViewModel toSquare)
    {
        if (IsAiThinking || Session.Status != GameStatus.InProgress || !IsHumanTurn())
            return false;

        var matchingMove = Session.LegalMoves.FirstOrDefault(m =>
            m.From == fromSquare.Position && m.To == toSquare.Position);

        if (matchingMove != null)
        {
            return ExecutePlayerMove(matchingMove);
        }

        // If not a legal move, select the source piece if it can move
        SquareClicked(fromSquare);
        return false;
    }

    [RelayCommand]
    public async Task NewGame()
    {
        if (!await ConfirmAbandonGameAsync())
            return;

        CancelAi();
        ClearSelection();
        Analysis.Reset();
        ResetClock();
        _engineHost.ClearTranspositionTable();
        FileName = string.Empty;
        IsGameModified = false;
        Session.StartNewGame();
        RefreshBoard();
    }

    [RelayCommand]
    public async Task Open()
    {
        if (_fileService == null)
            return;

        GameFile? file;
        GameRecord loaded;
        try
        {
            file = await _fileService.OpenAsync();
            if (file == null)
                return;

            loaded = GameRecordFormat.Parse(file.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _dialogService?.ShowError($"The game could not be opened.\n\n{ex.Message}");
            return;
        }

        CancelAi();
        ClearSelection();
        Analysis.Reset();
        _engineHost.ClearTranspositionTable();

        if (loaded.Tags.TryGetValue("GameMode", out var gmStr) &&
            Enum.TryParse<GameMode>(gmStr, true, out var gm))
        {
            GameMode = gm;
        }

        if (loaded.Tags.TryGetValue("TimeControlMode", out var modeStr) &&
            Enum.TryParse<TimeControlMode>(modeStr, true, out var mode))
        {
            int depth = loaded.Tags.TryGetValue("Depth", out var dStr) && int.TryParse(dStr, out var d) ? d : GameSettings.Default.Depth;
            int spm = loaded.Tags.TryGetValue("SecondsPerMove", out var sStr) && int.TryParse(sStr, out var s) ? s : GameSettings.Default.SecondsPerMove;
            int mpg = loaded.Tags.TryGetValue("MinutesPerGame", out var mStr) && int.TryParse(mStr, out var m) ? m : GameSettings.Default.MinutesPerGame;
            Settings = new GameSettings(mode, depth, spm, mpg).Normalize();
            ResetClock();
        }
        else if (loaded.Tags.TryGetValue("Difficulty", out var diffStr))
        {
            Settings = diffStr.ToLowerInvariant() switch
            {
                "easy" => new GameSettings(TimeControlMode.FixedDepth, 1, 5, 5),
                "hard" => new GameSettings(TimeControlMode.FixedDepth, 8, 5, 5),
                _ => new GameSettings(TimeControlMode.FixedDepth, 4, 5, 5),
            };
            ResetClock();
        }

        if (loaded.Tags.TryGetValue("Variant", out var varStr) &&
            Enum.TryParse<CheckersVariant>(varStr, true, out var parsedVar))
        {
            Settings = Settings with { Variant = parsedVar };
        }
        else
        {
            Settings = Settings with { Variant = loaded.Session.RuleEngine.Variant };
        }

        if (loaded.Tags.TryGetValue("TranspositionTableEntries", out var ttStr) &&
            int.TryParse(ttStr, out var parsedTtEntries))
        {
            Settings = (Settings with { TranspositionTableEntries = parsedTtEntries }).Normalize();
        }

        AttachSession(loaded.Session);
        FileName = file.Path;
        RefreshBoard();

        if (Session.MoveHistory.Count > 0)
        {
            HighlightLastMove(Session.MoveHistory[^1]);
        }
    }

    [RelayCommand]
    public Task Save() => WriteAsync(askForName: false);

    [RelayCommand]
    public Task SaveAs() => WriteAsync(askForName: true);

    private async Task WriteAsync(bool askForName)
    {
        if (_fileService == null)
            return;

        var tags = new Dictionary<string, string>
        {
            ["Variant"] = Settings.Variant.ToString(),
            ["GameMode"] = GameMode.ToString(),
            ["TimeControlMode"] = Settings.Mode.ToString(),
            ["Depth"] = Settings.Depth.ToString(),
            ["SecondsPerMove"] = Settings.SecondsPerMove.ToString(),
            ["MinutesPerGame"] = Settings.MinutesPerGame.ToString(),
            ["TranspositionTableEntries"] = Settings.TranspositionTableEntries.ToString()
        };

        string recordText = GameRecordFormat.Format(Session, tags);

        try
        {
            string? currentPath = string.IsNullOrEmpty(FileName) ? null : FileName;
            string? savedPath = await _fileService.SaveAsync(recordText + Environment.NewLine, currentPath, askForName);
            if (savedPath != null)
            {
                FileName = savedPath;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogService?.ShowError($"The game could not be saved.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    public void Undo()
    {
        if (Session.CanUndo)
        {
            if (IsVsComputer)
            {
                IsGameModified = true;
            }

            CancelAi();
            ClearSelection();
            Session.Undo();
            
            // If in vs Computer and it is now the computer's turn, undo one more step to return to Human
            if (GameMode == GameMode.HumanVsComputer && Session.CurrentState.ActivePlayer == PieceColor.Black && Session.CanUndo)
            {
                Session.Undo();
            }
            else if (GameMode == GameMode.ComputerVsHuman && Session.CurrentState.ActivePlayer == PieceColor.White && Session.CanUndo)
            {
                Session.Undo();
            }

            int currentPly = Session.MoveHistory.Count;
            if (_timeLeftAtPly.TryGetValue(currentPly, out var restoredTime))
            {
                _whiteTimeLeft = restoredTime.White;
                _blackTimeLeft = restoredTime.Black;
            }
            else
            {
                _whiteTimeLeft = Settings.GameTime;
                _blackTimeLeft = Settings.GameTime;
            }

            if (IsHumanVsComputerPlayer(PieceColor.White) && _whiteTimeLeft > TimeSpan.Zero)
                _hasHumanContinuedOnTime = false;
            else if (IsHumanVsComputerPlayer(PieceColor.Black) && _blackTimeLeft > TimeSpan.Zero)
                _hasHumanContinuedOnTime = false;

            _lastTickTimestamp = Session.MoveHistory.Count == 0 ? 0 : Stopwatch.GetTimestamp();
            OnPropertyChanged(nameof(WhiteTimeLeft));
            OnPropertyChanged(nameof(BlackTimeLeft));
            OnPropertyChanged(nameof(WhiteClockText));
            OnPropertyChanged(nameof(BlackClockText));
            OnPropertyChanged(nameof(ClockText));

            RefreshBoard();
        }
    }

    [RelayCommand]
    public void Redo()
    {
        if (Session.CanRedo)
        {
            CancelAi();
            ClearSelection();
            Session.Redo();
            RefreshBoard();
        }
    }

    [RelayCommand]
    public void ToggleSound()
    {
        IsSoundOn = !IsSoundOn;
    }

    [RelayCommand]
    public async Task SetGameMode(GameMode mode)
    {
        if (mode is GameMode.ComputerVsHuman or GameMode.HumanVsComputer)
        {
            if (!await ConfirmAbandonGameAsync())
                return;

            CancelAi();
            ClearSelection();
            Analysis.Reset();
            ResetClock();
            _engineHost.ClearTranspositionTable();
            FileName = string.Empty;
            IsGameModified = false;
            GameMode = mode;
            Session.StartNewGame();
            RefreshBoard();
        }
        else
        {
            if (GameMode != mode)
            {
                CancelAi();
                GameMode = mode;
                RefreshBoard();
            }
        }
    }

    private async Task<bool> ConfirmAbandonGameAsync()
    {
        if (_dialogService == null || Session.Status != GameStatus.InProgress || Session.MoveHistory.Count == 0)
            return true;

        return await _dialogService.ConfirmAsync(
            "Start New Game",
            "A game is currently in progress. Starting a new game will end the current game.\n\nDo you want to start a new game?");
    }

    [RelayCommand]
    public async Task EditSettings()
    {
        if (_dialogService == null)
            return;

        var updated = await _dialogService.EditSettingsAsync(Settings);
        if (updated != null && updated != Settings)
        {
            bool variantChanged = updated.Variant != Settings.Variant;
            if (variantChanged && Session.MoveHistory.Count > 0 && Session.Status == GameStatus.InProgress)
            {
                bool confirm = await _dialogService.ConfirmAsync(
                    "Change Checkers Variant",
                    "Changing the checkers variant requires starting a new game.\n\nDo you want to start a new game now with the new rules?");

                if (!confirm)
                {
                    updated = updated with { Variant = Settings.Variant };
                    variantChanged = false;
                }
            }

            if (!variantChanged && Session.MoveHistory.Count > 0 && Session.Status == GameStatus.InProgress && IsVsComputer)
            {
                IsGameModified = true;
            }

            // Only a new time control (or a new game) resets the clocks; other settings keep the time used so far.
            bool clockChanged = updated.Mode != Settings.Mode || updated.MinutesPerGame != Settings.MinutesPerGame;
            Settings = updated;
            if (clockChanged || variantChanged)
            {
                ResetClock();
            }

            if (variantChanged)
            {
                CancelAi();
                ClearSelection();
                Analysis.Reset();
                _engineHost.ClearTranspositionTable();
                FileName = string.Empty;
                AttachSession(new GameSession(new RuleEngine(Settings.Variant)));
            }

            RefreshBoard();
        }
    }

    [RelayCommand]
    public void About()
    {
        _dialogService?.ShowAbout();
    }

    private bool IsHumanTurn()
    {
        return GameMode switch
        {
            GameMode.HumanVsHuman => true,
            GameMode.HumanVsComputer => Session.CurrentState.ActivePlayer == PieceColor.White,
            GameMode.ComputerVsHuman => Session.CurrentState.ActivePlayer == PieceColor.Black,
            GameMode.ComputerVsComputer => false,
            _ => true
        };
    }

    private bool IsHumanVsComputerPlayer(PieceColor color) =>
        GameMode switch
        {
            GameMode.HumanVsComputer => color == PieceColor.White,
            GameMode.ComputerVsHuman => color == PieceColor.Black,
            _ => false
        };

    private void SelectPiece(Position pos, List<Move> validMoves)
    {
        _selectedPosition = pos;

        // Clear previous visual states (preserving last move & mandatory capture)
        foreach (var sq in BoardSquares)
        {
            sq.VisualState &= ~(SquareVisualState.Selected | SquareVisualState.ValidTarget | SquareVisualState.CapturedTarget);
        }

        // Highlight selected
        Squares[pos.Row, pos.Col].VisualState |= SquareVisualState.Selected;

        // Highlight valid landing destinations
        foreach (var move in validMoves)
        {
            Squares[move.To.Row, move.To.Col].VisualState |= SquareVisualState.ValidTarget;
        }

        // Highlight enemy pieces that will be captured
        bool isCapture = validMoves.Any(m => m.IsCapture);
        if (isCapture)
        {
            foreach (var move in validMoves)
            {
                foreach (var capPos in move.CapturedPositions)
                {
                    Squares[capPos.Row, capPos.Col].VisualState |= SquareVisualState.CapturedTarget;
                }
            }
        }

        var sqNum = Squares[pos.Row, pos.Col].DraughtsNumber?.ToString() ?? $"{pos.Row},{pos.Col}";
        var piece = Squares[pos.Row, pos.Col].Piece;
        bool isKing = piece.HasValue && piece.Value.IsKing;

        if (isCapture)
        {
            int maxCaps = validMoves.Max(m => m.CapturedPositions.Count);
            string kingPrefix = isKing ? (Session.RuleEngine.Variant == CheckersVariant.English ? "King " : "Flying King ") : "";
            string capPlural = maxCaps > 1 ? $"{maxCaps} pieces" : "1 piece";
            StatusText = $"{TurnIndicatorText} — Selected #{sqNum} ({kingPrefix}Multi-Jump captures {capPlural}!). Click a green landing circle.";
        }
        else
        {
            StatusText = $"{TurnIndicatorText} — Selected #{sqNum}. Click a green circle to move.";
        }
    }

    private void ClearSelection()
    {
        _selectedPosition = null;
        foreach (var sq in BoardSquares)
        {
            sq.VisualState &= ~(SquareVisualState.Selected | SquareVisualState.ValidTarget | SquareVisualState.CapturedTarget);
        }
    }

    public void HoverSquare(SquareViewModel? square)
    {
        if (square == null || !_selectedPosition.HasValue || !square.IsValidTarget)
            return;

        var matchingMove = Session.LegalMoves.FirstOrDefault(m =>
            m.From == _selectedPosition.Value && m.To == square.Position);

        if (matchingMove != null && matchingMove.IsCapture)
        {
            var pathIndices = string.Join(" ➔ ", matchingMove.Path.Select(p => p.ToDraughtsIndex()?.ToString() ?? $"({p.Row},{p.Col})"));
            var capIndices = string.Join(", ", matchingMove.CapturedPositions.Select(p => p.ToDraughtsIndex()?.ToString() ?? $"({p.Row},{p.Col})"));
            var fromIdx = _selectedPosition.Value.ToDraughtsIndex()?.ToString() ?? $"{_selectedPosition.Value.Row},{_selectedPosition.Value.Col}";
            StatusText = $"Route: {fromIdx} ➔ {pathIndices} (Capturing #{capIndices})";
        }
    }

    public void UnhoverSquare(SquareViewModel? square)
    {
        if (_selectedPosition.HasValue)
        {
            var movesForPiece = Session.LegalMoves.Where(m => m.From == _selectedPosition.Value).ToList();
            if (movesForPiece.Count > 0)
            {
                var sqNum = Squares[_selectedPosition.Value.Row, _selectedPosition.Value.Col].DraughtsNumber?.ToString() ?? "";
                var piece = Squares[_selectedPosition.Value.Row, _selectedPosition.Value.Col].Piece;
                bool isKing = piece.HasValue && piece.Value.IsKing;
                bool isCapture = movesForPiece.Any(m => m.IsCapture);
                if (isCapture)
                {
                    int maxCaps = movesForPiece.Max(m => m.CapturedPositions.Count);
                    string kingPrefix = isKing ? (Session.RuleEngine.Variant == CheckersVariant.English ? "King " : "Flying King ") : "";
                    string capPlural = maxCaps > 1 ? $"{maxCaps} pieces" : "1 piece";
                    StatusText = $"{TurnIndicatorText} — Selected #{sqNum} ({kingPrefix}Multi-Jump captures {capPlural}!). Click a green landing circle.";
                }
                else
                {
                    StatusText = $"{TurnIndicatorText} — Selected #{sqNum}. Click a green circle to move.";
                }
            }
        }
    }

    private void HighlightLastMove(Move move)
    {
        // Clear previous last move markers
        foreach (var sq in BoardSquares)
        {
            sq.VisualState &= ~(SquareVisualState.LastMoveFrom | SquareVisualState.LastMoveTo);
        }

        Squares[move.From.Row, move.From.Col].VisualState |= SquareVisualState.LastMoveFrom;
        Squares[move.To.Row, move.To.Col].VisualState |= SquareVisualState.LastMoveTo;
    }

    private void OnMoveExecuted()
    {
        int ply = Session.MoveHistory.Count;
        _timeLeftAtPly[ply] = (_whiteTimeLeft, _blackTimeLeft);
        _lastTickTimestamp = Stopwatch.GetTimestamp();
        RefreshBoard();
    }

    public void RefreshBoard()
    {
        var state = Session.CurrentState;

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                Squares[r, c].Piece = state.GetPiece(r, c);
            }
        }

        WhitePiecesCount = state.WhitePiecesCount;
        BlackPiecesCount = state.BlackPiecesCount;
        WhiteKingsCount = state.WhiteKingsCount;
        BlackKingsCount = state.BlackKingsCount;

        CanUndo = Session.CanUndo && !IsAiThinking;
        CanRedo = Session.CanRedo && !IsAiThinking;

        // Counts individual moves (both players), the same in all four games.
        int moveCount = Session.MoveHistory.Count;
        MoveCountText = moveCount == 1 ? "1 move" : $"{moveCount} moves";

        // Update MoveHistoryList
        MoveHistoryList.Clear();
        foreach (var m in Session.MoveHistory)
        {
            MoveHistoryList.Add(m);
        }

        // Highlight mandatory capture sources and movable pieces
        bool hasCaptures = Session.LegalMoves.Any(m => m.IsCapture);
        bool isHuman = IsHumanTurn() && Session.Status == GameStatus.InProgress && !IsAiThinking;

        foreach (var sq in BoardSquares)
        {
            sq.VisualState &= ~SquareVisualState.MandatoryCaptureSource;
            if (hasCaptures && Session.LegalMoves.Any(m => m.From == sq.Position))
            {
                sq.VisualState |= SquareVisualState.MandatoryCaptureSource;
            }

            bool canMove = isHuman && sq.HasPiece &&
                           sq.Piece!.Value.Color == state.ActivePlayer &&
                           Session.LegalMoves.Any(m => m.From == sq.Position);
            sq.IsMovable = canMove;
            if (canMove)
            {
                sq.VisualState |= SquareVisualState.Movable;
            }
            else
            {
                sq.VisualState &= ~SquareVisualState.Movable;
            }
        }

        UpdateStatusText();
        SwitchSidesCommand.NotifyCanExecuteChanged();

        // Check if an AI turn needs to be triggered
        CheckAndTriggerAiTurn();
    }

    private void UpdateStatusText()
    {
        if (Session.Status == GameStatus.WhiteWon)
        {
            StatusText = Session.GameOverReason == GameOverReason.Timeout
                ? "Game Over — White Won on time!"
                : "Game Over — White Won!";
            return;
        }
        if (Session.Status == GameStatus.BlackWon)
        {
            StatusText = Session.GameOverReason == GameOverReason.Timeout
                ? "Game Over — Black Won on time!"
                : "Game Over — Black Won!";
            return;
        }
        if (Session.Status == GameStatus.Draw)
        {
            StatusText = $"Game Drawn ({FormatDrawReason(Session.GameOverReason)})";
            return;
        }

        var active = Session.CurrentState.ActivePlayer;
        TurnIndicatorText = active == PieceColor.White ? "White's Turn" : "Black's Turn";

        if (IsAiThinking)
        {
            StatusText = $"{TurnIndicatorText} (AI Thinking...)";
        }
        else if (Session.LegalMoves.Any(m => m.IsCapture))
        {
            StatusText = $"{TurnIndicatorText} (Mandatory Jump! Select a red-outlined piece)";
        }
        else
        {
            StatusText = $"{TurnIndicatorText} — Click a piece to move it";
        }
    }

    private async void CheckAndTriggerAiTurn()
    {
        if (Session.Status != GameStatus.InProgress || IsHumanTurn())
            return;

        CancelAi();
        _aiCts = new CancellationTokenSource();
        _moveNowCts = new CancellationTokenSource();
        var token = _aiCts.Token;
        var moveNowToken = _moveNowCts.Token;

        IsAiThinking = true;
        CanUndo = false;
        CanRedo = false;
        UpdateStatusText();
        SwitchSidesCommand.NotifyCanExecuteChanged();

        int plyBefore = Session.MoveHistory.Count;
        _timeLeftAtPly[plyBefore] = (_whiteTimeLeft, _blackTimeLeft);

        int searchId = ++_searchId;
        Analysis.Reset();

        var progress = new Progress<SearchAnalysis>(info =>
        {
            if (searchId == _searchId && IsAiThinking && !token.IsCancellationRequested)
            {
                Analysis.Update(info);
            }
        });

        try
        {
            var legalMoves = Session.LegalMoves;

            // Small delay for natural feel
            if (AiDelayMs > 0)
            {
                await Task.Delay(AiDelayMs, token);
            }

            var stateHashes = Session.StateHistory.Select(s => s.ZobristHash).ToList();
            var computerTime = Session.CurrentState.ActivePlayer == PieceColor.White ? _whiteTimeLeft : _blackTimeLeft;
            var (move, lastAnalysis) = await _engineHost.ChooseMoveAsync(
                Session.CurrentState, legalMoves, Settings, computerTime, stateHashes, progress, token, moveNowToken);

            if (searchId == _searchId && !token.IsCancellationRequested && lastAnalysis != null)
            {
                Analysis.Update(lastAnalysis);
            }

            if (searchId == _searchId && !token.IsCancellationRequested && Session.Status == GameStatus.InProgress)
            {
                bool success = Session.TryMakeMove(move);
                if (success)
                {
                    if (move.IsPromotion)
                        _soundService.Play(SoundType.King);
                    else if (move.IsCapture)
                        _soundService.Play(SoundType.Capture);
                    else
                        _soundService.Play(SoundType.Move);

                    HighlightLastMove(move);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when reset or undone
        }
        finally
        {
            _moveNowCts?.Dispose();
            _moveNowCts = null;

            if (searchId == _searchId)
            {
                IsAiThinking = false;
                CanUndo = Session.CanUndo;
                CanRedo = Session.CanRedo;
                UpdateStatusText();
                SwitchSidesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Optional factory to instantiate AI players (e.g. for testing or custom engines).
    /// </summary>
    public Func<GameSettings, TimeSpan, IPlayer>? AiPlayerFactory
    {
        get => _localEngineHost.PlayerFactory;
        set => _localEngineHost.PlayerFactory = value;
    }

    public IPlayer CreateAiPlayer() => _localEngineHost.CreatePlayer(Settings, ComputerTimeLeft);

    public void CancelAi()
    {
        _searchId++;
        if (_moveNowCts != null)
        {
            _moveNowCts.Cancel();
            _moveNowCts.Dispose();
            _moveNowCts = null;
        }
        if (_aiCts != null)
        {
            _aiCts.Cancel();
            _aiCts.Dispose();
            _aiCts = null;
        }
        IsAiThinking = false;
    }

    private void HandleGameOver(GameOverEventArgs e)
    {
        _lastTickTimestamp = 0;
        RefreshBoard();
        if (e.Status is GameStatus.WhiteWon or GameStatus.BlackWon)
        {
            _soundService.Play(SoundType.Win);
        }
    }

    private void OnClockTimerTick(object? state)
    {
        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => TickClock(), null);
        }
        else
        {
            TickClock();
        }
    }

    private void TickClock()
    {
        if (_isTimeoutPrompting ||
            Settings.Mode != TimeControlMode.TimePerGame ||
            Session.Status != GameStatus.InProgress ||
            Session.MoveHistory.Count == 0)
        {
            _lastTickTimestamp = 0;
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (_lastTickTimestamp == 0)
        {
            _lastTickTimestamp = now;
            return;
        }

        double elapsedSeconds = (now - _lastTickTimestamp) / (double)Stopwatch.Frequency;
        _lastTickTimestamp = now;

        if (elapsedSeconds <= 0)
            return;

        var elapsed = TimeSpan.FromSeconds(elapsedSeconds);
        var active = Session.CurrentState.ActivePlayer;

        if (active == PieceColor.White)
        {
            if (IsHumanVsComputerPlayer(PieceColor.White) && _hasHumanContinuedOnTime)
            {
                _whiteTimeLeft = TimeSpan.Zero;
            }
            else
            {
                _whiteTimeLeft = _whiteTimeLeft > elapsed ? _whiteTimeLeft - elapsed : TimeSpan.Zero;
                if (_whiteTimeLeft <= TimeSpan.Zero)
                {
                    _whiteTimeLeft = TimeSpan.Zero;
                    _ = HandleTimeoutAsync(PieceColor.White);
                    return;
                }
            }
        }
        else
        {
            if (IsHumanVsComputerPlayer(PieceColor.Black) && _hasHumanContinuedOnTime)
            {
                _blackTimeLeft = TimeSpan.Zero;
            }
            else
            {
                _blackTimeLeft = _blackTimeLeft > elapsed ? _blackTimeLeft - elapsed : TimeSpan.Zero;
                if (_blackTimeLeft <= TimeSpan.Zero)
                {
                    _blackTimeLeft = TimeSpan.Zero;
                    _ = HandleTimeoutAsync(PieceColor.Black);
                    return;
                }
            }
        }

        OnPropertyChanged(nameof(WhiteTimeLeft));
        OnPropertyChanged(nameof(BlackTimeLeft));
        OnPropertyChanged(nameof(WhiteClockText));
        OnPropertyChanged(nameof(BlackClockText));
        OnPropertyChanged(nameof(ClockText));
    }

    public async Task HandleTimeoutAsync(PieceColor timedOutPlayer)
    {
        if (Session.Status != GameStatus.InProgress)
            return;

        _lastTickTimestamp = 0;

        if (IsHumanVsComputerPlayer(timedOutPlayer) && !_hasHumanContinuedOnTime)
        {
            _isTimeoutPrompting = true;
            bool continuePlaying = false;
            try
            {
                if (_dialogService != null)
                {
                    continuePlaying = await _dialogService.ConfirmAsync(
                        "Time Expired",
                        "You ran out of time!\n\nDo you want to continue playing?");
                }
            }
            finally
            {
                _isTimeoutPrompting = false;
            }

            if (continuePlaying)
            {
                _hasHumanContinuedOnTime = true;
                IsGameModified = true;

                if (timedOutPlayer == PieceColor.White)
                    _whiteTimeLeft = TimeSpan.Zero;
                else
                    _blackTimeLeft = TimeSpan.Zero;

                OnPropertyChanged(nameof(WhiteTimeLeft));
                OnPropertyChanged(nameof(BlackTimeLeft));
                OnPropertyChanged(nameof(WhiteClockText));
                OnPropertyChanged(nameof(BlackClockText));
                OnPropertyChanged(nameof(ClockText));
                RefreshBoard();
                return;
            }
        }

        CancelAi();
        Session.FlagFell(timedOutPlayer);
        RefreshBoard();
    }

    public void Dispose()
    {
        CancelAi();
        _clockTimer.Dispose();
    }

    private static string FormatDrawReason(GameOverReason reason) => reason switch
    {
        GameOverReason.FortyMoveRuleWithoutCaptureOrPromotion => "40-Move Rule",
        GameOverReason.ThreefoldRepetition => "Threefold Repetition",
        _ => "Mutual Agreement"
    };

    private sealed class DummySoundService : ISoundService
    {
        public bool IsEnabled { get; set; } = true;
        public void Play(SoundType sound) { }
    }
}
