using Checkers.Core.Engine;

namespace Checkers.Core.Models;

/// <summary>
/// Snapshot representing the complete state of the 8x8 checkers board.
/// </summary>
public sealed class BoardState
{
    private readonly Piece?[,] _grid;

    public PieceColor ActivePlayer { get; set; } = PieceColor.White;
    public int HalfMoveClock { get; set; } = 0;
    public int FullMoveNumber { get; set; } = 1;
    public ulong ZobristHash { get; set; }

    public int WhitePiecesCount { get; private set; }
    public int BlackPiecesCount { get; private set; }
    public int WhiteKingsCount { get; private set; }
    public int BlackKingsCount { get; private set; }

    public BoardState()
    {
        _grid = new Piece?[8, 8];
    }

    private BoardState(Piece?[,] grid)
    {
        _grid = (Piece?[,])grid.Clone();
    }

    public Piece? GetPiece(Position pos) =>
        pos.IsValid ? _grid[pos.Row, pos.Col] : null;

    public Piece? GetPiece(int row, int col) =>
        (row is >= 0 and < 8 && col is >= 0 and < 8) ? _grid[row, col] : null;

    public void SetPiece(Position pos, Piece? piece)
    {
        if (!pos.IsValid)
            throw new ArgumentOutOfRangeException(nameof(pos), "Position is outside board bounds.");

        var current = _grid[pos.Row, pos.Col];
        if (current.HasValue)
        {
            if (current.Value.Color == PieceColor.White)
            {
                WhitePiecesCount--;
                if (current.Value.IsKing) WhiteKingsCount--;
            }
            else
            {
                BlackPiecesCount--;
                if (current.Value.IsKing) BlackKingsCount--;
            }
        }

        _grid[pos.Row, pos.Col] = piece;

        if (piece.HasValue)
        {
            if (piece.Value.Color == PieceColor.White)
            {
                WhitePiecesCount++;
                if (piece.Value.IsKing) WhiteKingsCount++;
            }
            else
            {
                BlackPiecesCount++;
                if (piece.Value.IsKing) BlackKingsCount++;
            }
        }
    }

    public ulong RecalculateHash()
    {
        ulong hash = Zobrist.GetTurnKey(ActivePlayer);
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var piece = _grid[r, c];
                if (piece.HasValue)
                {
                    hash ^= Zobrist.GetPieceKey(r, c, piece.Value);
                }
            }
        }
        ZobristHash = hash;
        return hash;
    }

    public BoardState Clone()
    {
        var clone = new BoardState(_grid)
        {
            ActivePlayer = this.ActivePlayer,
            HalfMoveClock = this.HalfMoveClock,
            FullMoveNumber = this.FullMoveNumber,
            ZobristHash = this.ZobristHash,
            WhitePiecesCount = this.WhitePiecesCount,
            BlackPiecesCount = this.BlackPiecesCount,
            WhiteKingsCount = this.WhiteKingsCount,
            BlackKingsCount = this.BlackKingsCount
        };
        return clone;
    }

    public static BoardState CreateEmpty(PieceColor activePlayer = PieceColor.White)
    {
        var state = new BoardState
        {
            ActivePlayer = activePlayer
        };
        state.RecalculateHash();
        return state;
    }

    public static BoardState CreateInitial()
    {
        var state = new BoardState
        {
            ActivePlayer = PieceColor.White,
            HalfMoveClock = 0,
            FullMoveNumber = 1
        };

        // Black pieces on dark squares of rows 0, 1, 2
        for (int r = 0; r <= 2; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var pos = new Position(r, c);
                if (pos.IsDarkSquare)
                {
                    state.SetPiece(pos, new Piece(PieceColor.Black, PieceType.Man));
                }
            }
        }

        // White pieces on dark squares of rows 5, 6, 7
        for (int r = 5; r <= 7; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var pos = new Position(r, c);
                if (pos.IsDarkSquare)
                {
                    state.SetPiece(pos, new Piece(PieceColor.White, PieceType.Man));
                }
            }
        }

        state.RecalculateHash();
        return state;
    }
}
