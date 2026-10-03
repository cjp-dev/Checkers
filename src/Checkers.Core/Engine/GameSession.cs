using Checkers.Core.Models;

namespace Checkers.Core.Engine;

public sealed class MoveExecutedEventArgs : EventArgs
{
    public required Move Move { get; init; }
    public required BoardState ResultingState { get; init; }
}

public sealed class GameOverEventArgs : EventArgs
{
    public required GameStatus Status { get; init; }
    public required GameOverReason Reason { get; init; }
}

/// <summary>
/// Manages the state, move execution, undo/redo stacks, and game lifecycle.
/// </summary>
public sealed class GameSession
{
    private readonly IRuleEngine _ruleEngine;
    public IRuleEngine RuleEngine => _ruleEngine;
    private readonly List<BoardState> _stateHistory = [];
    private readonly List<Move> _moveHistory = [];
    private readonly Stack<(BoardState State, Move Move)> _redoStack = [];

    public BoardState CurrentState { get; private set; }
    public GameStatus Status { get; private set; } = GameStatus.InProgress;
    public GameOverReason GameOverReason { get; private set; } = GameOverReason.None;
    public IReadOnlyList<Move> LegalMoves { get; private set; } = [];
    public IReadOnlyList<Move> MoveHistory => _moveHistory.AsReadOnly();
    public IReadOnlyList<BoardState> StateHistory => _stateHistory.AsReadOnly();

    public bool CanUndo => _stateHistory.Count > 1 && Status == GameStatus.InProgress;
    public bool CanRedo => _redoStack.Count > 0 && Status == GameStatus.InProgress;

    public event EventHandler<MoveExecutedEventArgs>? MoveExecuted;
    public event EventHandler<GameOverEventArgs>? GameOver;

    public GameSession(IRuleEngine? ruleEngine = null)
    {
        _ruleEngine = ruleEngine ?? EngineFactory.CreateRuleEngine();
        CurrentState = BoardState.CreateInitial();
        StartNewGame();
    }

    public void StartNewGame()
    {
        _stateHistory.Clear();
        _moveHistory.Clear();
        _redoStack.Clear();

        CurrentState = BoardState.CreateInitial();
        _stateHistory.Add(CurrentState);

        UpdateGameState();
    }

    public void StartFromState(BoardState customState)
    {
        _stateHistory.Clear();
        _moveHistory.Clear();
        _redoStack.Clear();

        CurrentState = customState.Clone();
        _stateHistory.Add(CurrentState);

        UpdateGameState();
    }

    public bool TryMakeMove(Move move)
    {
        if (Status != GameStatus.InProgress)
            return false;

        if (!_ruleEngine.IsLegalMove(CurrentState, move))
            return false;

        // Apply move
        var nextState = _ruleEngine.ApplyMove(CurrentState, move);

        _moveHistory.Add(move);
        _stateHistory.Add(nextState);
        _redoStack.Clear();

        CurrentState = nextState;

        UpdateGameState();

        MoveExecuted?.Invoke(this, new MoveExecutedEventArgs
        {
            Move = move,
            ResultingState = CurrentState
        });

        if (Status != GameStatus.InProgress)
        {
            GameOver?.Invoke(this, new GameOverEventArgs
            {
                Status = Status,
                Reason = GameOverReason
            });
        }

        return true;
    }

    public bool Undo()
    {
        if (!CanUndo)
            return false;

        int lastIndex = _stateHistory.Count - 1;
        var lastState = _stateHistory[lastIndex];
        var lastMove = _moveHistory[_moveHistory.Count - 1];

        _stateHistory.RemoveAt(lastIndex);
        _moveHistory.RemoveAt(_moveHistory.Count - 1);
        _redoStack.Push((lastState, lastMove));

        CurrentState = _stateHistory[^1];
        UpdateGameState();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
            return false;

        var (redoState, redoMove) = _redoStack.Pop();
        _stateHistory.Add(redoState);
        _moveHistory.Add(redoMove);

        CurrentState = redoState;
        UpdateGameState();

        MoveExecuted?.Invoke(this, new MoveExecutedEventArgs
        {
            Move = redoMove,
            ResultingState = CurrentState
        });

        return true;
    }

    private void UpdateGameState()
    {
        var hashes = _stateHistory.Select(s => s.ZobristHash).ToList();
        var (status, reason) = _ruleEngine.EvaluateGameStatus(CurrentState, hashes);

        Status = status;
        GameOverReason = reason;

        LegalMoves = (Status == GameStatus.InProgress)
            ? _ruleEngine.GetLegalMoves(CurrentState)
            : [];
    }
}
