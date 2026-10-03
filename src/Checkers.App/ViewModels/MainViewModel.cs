using System.Collections.ObjectModel;
using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.Core.AI;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Checkers.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISoundService _soundService;
    private readonly IDialogService? _dialogService;
    private readonly IGameFileService? _fileService;
    private Position? _selectedPosition;
    private CancellationTokenSource? _aiCts;
    private int _searchId;

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
    private bool _isAiThinking;

    [ObservableProperty]
    private GameMode _gameMode = GameMode.HumanVsHuman;

    [ObservableProperty]
    private GameSettings _settings = GameSettings.Default;

    private TranspositionTable _transpositionTable = TranspositionTable.FromEntries(GameSettings.DefaultTranspositionTableEntries);

    private TimeSpan _computerTimeLeft;
    private readonly Dictionary<int, TimeSpan> _timeLeftAtPly = [];

    public bool IsHumanVsComputer => GameMode == GameMode.HumanVsComputer;
    public bool IsHumanVsHuman => GameMode == GameMode.HumanVsHuman;

    public string SettingsBadgeText => Settings.Summary;
    public string VariantBadgeText => Settings.VariantBadgeText;

    public bool IsClockVisible => Settings.Mode == TimeControlMode.TimePerGame && GameMode == GameMode.HumanVsComputer;

    public string ClockText => IsClockVisible
        ? $"Clock: {FormatClock(_computerTimeLeft)}"
        : string.Empty;

    public TimeSpan ComputerTimeLeft => _computerTimeLeft;

    private static string FormatClock(TimeSpan span) => span < TimeSpan.Zero ? "0:00" : $"{(int)span.TotalMinutes}:{span.Seconds:00}";

    public string GameModeBadgeText => GameMode switch
    {
        GameMode.HumanVsComputer => "vs Computer",
        GameMode.HumanVsHuman => "vs Human",
        GameMode.ComputerVsComputer => "Computer vs Computer",
        _ => "Checkers"
    };

    public string GameModeDescription => GameMode switch
    {
        GameMode.HumanVsComputer => $"vs Computer ({Settings.Summary})",
        GameMode.HumanVsHuman => "Human vs Human",
        GameMode.ComputerVsComputer => "Computer vs Computer",
        _ => string.Empty
    };

    public string WhitePlayerLabel => GameMode switch
    {
        GameMode.HumanVsComputer => "Human (You)",
        GameMode.HumanVsHuman => "Player 1",
        GameMode.ComputerVsComputer => "Computer 1",
        _ => "White"
    };

    public string BlackPlayerLabel => GameMode switch
    {
        GameMode.HumanVsComputer => $"Computer ({Settings.Summary})",
        GameMode.HumanVsHuman => "Player 2",
        GameMode.ComputerVsComputer => "Computer 2",
        _ => "Black"
    };

    public AnalysisViewModel Analysis { get; } = new();

    [ObservableProperty]
    private bool _isAnalysisVisible = true;

    public bool ShowAnalysis => IsAnalysisVisible && GameMode == GameMode.HumanVsComputer;

    [RelayCommand]
    public void ToggleAnalysis()
    {
        IsAnalysisVisible = !IsAnalysisVisible;
    }

    partial void OnGameModeChanged(GameMode value)
    {
        OnPropertyChanged(nameof(IsHumanVsComputer));
        OnPropertyChanged(nameof(IsHumanVsHuman));
        OnPropertyChanged(nameof(ShowAnalysis));
        OnPropertyChanged(nameof(GameModeBadgeText));
        OnPropertyChanged(nameof(GameModeDescription));
        OnPropertyChanged(nameof(WhitePlayerLabel));
        OnPropertyChanged(nameof(BlackPlayerLabel));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(IsClockVisible));
    }

    partial void OnIsAnalysisVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAnalysis));
    }

    partial void OnSettingsChanging(GameSettings? oldValue, GameSettings newValue)
    {
        if (oldValue != null && oldValue.Variant != newValue.Variant && _transpositionTable.Capacity == newValue.TranspositionTableEntries)
        {
            _transpositionTable.Clear();
        }
    }

    partial void OnSettingsChanged(GameSettings value)
    {
        if (_transpositionTable.Capacity != value.TranspositionTableEntries)
        {
            _transpositionTable = TranspositionTable.FromEntries(value.TranspositionTableEntries);
        }

        OnPropertyChanged(nameof(SettingsBadgeText));
        OnPropertyChanged(nameof(VariantBadgeText));
        OnPropertyChanged(nameof(GameModeDescription));
        OnPropertyChanged(nameof(BlackPlayerLabel));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(IsClockVisible));
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
        IGameFileService? fileService = null)
    {
        Session = session ?? new GameSession(new RuleEngine(Settings.Variant));
        _soundService = soundService ?? new DummySoundService();
        _dialogService = dialogService;
        _fileService = fileService;
        _computerTimeLeft = Settings.GameTime;

        // Initialize 8x8 squares
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var sq = new SquareViewModel(r, c);
                Squares[r, c] = sq;
                BoardSquares.Add(sq);
            }
        }

        Session.MoveExecuted += OnMoveExecutedHandler;
        Session.GameOver += OnGameOverHandler;

        RefreshBoard();
    }

    public void ResetClock()
    {
        _computerTimeLeft = Settings.GameTime;
        _timeLeftAtPly.Clear();
        OnPropertyChanged(nameof(ClockText));
    }

    private void AttachSession(GameSession newSession)
    {
        Session.MoveExecuted -= OnMoveExecutedHandler;
        Session.GameOver -= OnGameOverHandler;

        Session = newSession;

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
                    StatusText = $"Piece on square {sqNum} has no legal moves (blocked). Select a highlighted piece.";
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
    public void NewGame()
    {
        CancelAi();
        ClearSelection();
        Analysis.Reset();
        ResetClock();
        _transpositionTable.Clear();
        FileName = string.Empty;
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
        _transpositionTable.Clear();

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
            CancelAi();
            ClearSelection();
            Session.Undo();
            
            // If in HumanVsComputer and it is now the computer's turn, undo one more step to return to Human
            if (GameMode == GameMode.HumanVsComputer && Session.CurrentState.ActivePlayer == PieceColor.Black && Session.CanUndo)
            {
                Session.Undo();
            }

            int currentPly = Session.MoveHistory.Count;
            if (_timeLeftAtPly.TryGetValue(currentPly, out var restoredTime))
            {
                _computerTimeLeft = restoredTime;
                OnPropertyChanged(nameof(ClockText));
            }

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
        _soundService.IsEnabled = IsSoundOn;
    }

    [RelayCommand]
    public void SetGameMode(GameMode mode)
    {
        if (GameMode != mode)
        {
            CancelAi();
            GameMode = mode;
            RefreshBoard();
        }
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

            Settings = updated;
            ResetClock();

            if (variantChanged)
            {
                CancelAi();
                ClearSelection();
                Analysis.Reset();
                _transpositionTable.Clear();
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
            GameMode.ComputerVsComputer => false,
            _ => true
        };
    }

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
            StatusText = $"{TurnIndicatorText} — Selected #{sqNum}. Click or drag to a green circle to move.";
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
                    StatusText = $"{TurnIndicatorText} — Selected #{sqNum}. Click or drag to a green circle to move.";
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

        MoveCountText = $"Move {state.FullMoveNumber}";

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

        // Check if an AI turn needs to be triggered
        CheckAndTriggerAiTurn();
    }

    private void UpdateStatusText()
    {
        if (Session.Status == GameStatus.WhiteWon)
        {
            StatusText = "Game Over — White Won!";
            return;
        }
        if (Session.Status == GameStatus.BlackWon)
        {
            StatusText = "Game Over — Black Won!";
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
            StatusText = $"{TurnIndicatorText} — Click or drag a highlighted piece to move";
        }
    }

    private async void CheckAndTriggerAiTurn()
    {
        if (Session.Status != GameStatus.InProgress || IsHumanTurn())
            return;

        CancelAi();
        _aiCts = new CancellationTokenSource();
        var token = _aiCts.Token;

        IsAiThinking = true;
        CanUndo = false;
        CanRedo = false;
        UpdateStatusText();

        int plyBefore = Session.MoveHistory.Count;
        _timeLeftAtPly[plyBefore] = _computerTimeLeft;

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
            var ai = CreateAiPlayer();
            var legalMoves = Session.LegalMoves;

            // Small delay for natural feel
            if (AiDelayMs > 0)
            {
                await Task.Delay(AiDelayMs, token);
            }

            var stateHashes = Session.StateHistory.Select(s => s.ZobristHash).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var move = await Task.Run(async () => await ai.GetMoveAsync(Session.CurrentState, legalMoves, progress, stateHashes, token), token);
            sw.Stop();

            if (Settings.Mode == TimeControlMode.TimePerGame)
            {
                _computerTimeLeft = _computerTimeLeft > sw.Elapsed ? _computerTimeLeft - sw.Elapsed : TimeSpan.Zero;
                OnPropertyChanged(nameof(ClockText));
            }

            if (searchId == _searchId && !token.IsCancellationRequested && ai.LastAnalysis != null)
            {
                Analysis.Update(ai.LastAnalysis);
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
            if (searchId == _searchId)
            {
                IsAiThinking = false;
                CanUndo = Session.CanUndo;
                CanRedo = Session.CanRedo;
                UpdateStatusText();
            }
        }
    }

    /// <summary>
    /// Optional factory to instantiate AI players (e.g. for testing or custom engines).
    /// </summary>
    public Func<GameSettings, TimeSpan, IPlayer>? AiPlayerFactory { get; set; }

    public IPlayer CreateAiPlayer() =>
        AiPlayerFactory?.Invoke(Settings, _computerTimeLeft) ?? new MinimaxPlayer(
            Settings.ToLimits(_computerTimeLeft),
            ruleEngine: new RuleEngine(Settings.Variant),
            evaluator: new EvaluationFunction(Settings.Variant),
            transpositionTable: _transpositionTable);

    public void CancelAi()
    {
        _searchId++;
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
        RefreshBoard();
        if (e.Status is GameStatus.WhiteWon or GameStatus.BlackWon)
        {
            _soundService.Play(SoundType.Win);
        }
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
