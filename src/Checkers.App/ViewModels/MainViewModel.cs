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
    private Position? _selectedPosition;
    private CancellationTokenSource? _aiCts;

    public GameSession Session { get; }
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
    private AiDifficulty _difficulty = AiDifficulty.Medium;

    public string Title => "Checkers (Draughts)";

    public MainViewModel(
        GameSession? session = null,
        ISoundService? soundService = null,
        IDialogService? dialogService = null)
    {
        Session = session ?? new GameSession();
        _soundService = soundService ?? new DummySoundService();
        _dialogService = dialogService;

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

        Session.MoveExecuted += (s, e) => OnMoveExecuted();
        Session.GameOver += (s, e) => HandleGameOver(e);

        RefreshBoard();
    }

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
                bool success = Session.TryMakeMove(matchingMove);
                if (success)
                {
                    // Play move audio
                    if (matchingMove.IsPromotion)
                        _soundService.Play(SoundType.King);
                    else if (matchingMove.IsCapture)
                        _soundService.Play(SoundType.Capture);
                    else
                        _soundService.Play(SoundType.Move);

                    ClearSelection();
                    HighlightLastMove(matchingMove);
                    return;
                }
            }
        }

        // 2. Clicked on the currently selected piece -> Deselect
        if (_selectedPosition.HasValue && square.Position == _selectedPosition.Value)
        {
            ClearSelection();
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
        }

        // 4. Clicked elsewhere -> Deselect
        ClearSelection();
    }

    [RelayCommand]
    public void NewGame()
    {
        CancelAi();
        ClearSelection();
        Session.StartNewGame();
        RefreshBoard();
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
    public void SetDifficulty(AiDifficulty difficulty)
    {
        if (Difficulty != difficulty)
        {
            Difficulty = difficulty;
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

        // Clear previous visual states (preserving last move)
        foreach (var sq in BoardSquares)
        {
            sq.VisualState &= ~(SquareVisualState.Selected | SquareVisualState.ValidTarget);
        }

        // Highlight selected
        Squares[pos.Row, pos.Col].VisualState |= SquareVisualState.Selected;

        // Highlight valid landing destinations
        foreach (var move in validMoves)
        {
            Squares[move.To.Row, move.To.Col].VisualState |= SquareVisualState.ValidTarget;
        }
    }

    private void ClearSelection()
    {
        _selectedPosition = null;
        foreach (var sq in BoardSquares)
        {
            sq.VisualState &= ~(SquareVisualState.Selected | SquareVisualState.ValidTarget);
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

        // Highlight mandatory capture sources if captures are forced
        bool hasCaptures = Session.LegalMoves.Any(m => m.IsCapture);
        foreach (var sq in BoardSquares)
        {
            sq.VisualState &= ~SquareVisualState.MandatoryCaptureSource;
            if (hasCaptures && Session.LegalMoves.Any(m => m.From == sq.Position))
            {
                sq.VisualState |= SquareVisualState.MandatoryCaptureSource;
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
            StatusText = $"{TurnIndicatorText} (Mandatory Jump!)";
        }
        else
        {
            StatusText = TurnIndicatorText;
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

        try
        {
            var ai = CreateAiPlayer();
            var legalMoves = Session.LegalMoves;

            // Small delay for natural feel
            await Task.Delay(250, token);

            var move = await Task.Run(async () => await ai.GetMoveAsync(Session.CurrentState, legalMoves, token), token);

            if (!token.IsCancellationRequested && Session.Status == GameStatus.InProgress)
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
            IsAiThinking = false;
            CanUndo = Session.CanUndo;
            CanRedo = Session.CanRedo;
            UpdateStatusText();
        }
    }

    public IPlayer CreateAiPlayer() => Difficulty switch
    {
        AiDifficulty.Easy => new RandomPlayer("Easy AI"),
        AiDifficulty.Medium => new MinimaxPlayer(depth: 3, name: "Medium AI"),
        AiDifficulty.Hard => new MinimaxPlayer(depth: 6, name: "Hard AI"),
        _ => new MinimaxPlayer(depth: 3)
    };

    public void CancelAi()
    {
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
