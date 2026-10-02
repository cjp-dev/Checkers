using Checkers.App.Models;
using Checkers.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Checkers.App.ViewModels;

public sealed partial class SquareViewModel : ObservableObject
{
    public Position Position { get; }
    public int Row => Position.Row;
    public int Col => Position.Col;
    public bool IsDark => Position.IsDarkSquare;
    public int? DraughtsNumber => Position.ToDraughtsIndex();

    [ObservableProperty]
    private Piece? _piece;

    [ObservableProperty]
    private SquareVisualState _visualState;

    [ObservableProperty]
    private bool _isMovable;

    public bool HasPiece => Piece.HasValue;
    public bool IsWhitePiece => Piece.HasValue && Piece.Value.Color == PieceColor.White;
    public bool IsBlackPiece => Piece.HasValue && Piece.Value.Color == PieceColor.Black;
    public bool IsKing => Piece.HasValue && Piece.Value.IsKing;

    public bool IsSelected => (VisualState & SquareVisualState.Selected) != 0;
    public bool IsValidTarget => (VisualState & SquareVisualState.ValidTarget) != 0;
    public bool IsLastMove => (VisualState & (SquareVisualState.LastMoveFrom | SquareVisualState.LastMoveTo)) != 0;
    public bool IsMandatoryCaptureSource => (VisualState & SquareVisualState.MandatoryCaptureSource) != 0;

    public bool CanInteract => IsMovable || IsValidTarget || IsSelected;

    public SquareViewModel(int row, int col)
    {
        Position = new Position(row, col);
    }

    partial void OnPieceChanged(Piece? value)
    {
        OnPropertyChanged(nameof(HasPiece));
        OnPropertyChanged(nameof(IsWhitePiece));
        OnPropertyChanged(nameof(IsBlackPiece));
        OnPropertyChanged(nameof(IsKing));
        OnPropertyChanged(nameof(CanInteract));
    }

    partial void OnVisualStateChanged(SquareVisualState value)
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(IsValidTarget));
        OnPropertyChanged(nameof(IsLastMove));
        OnPropertyChanged(nameof(IsMandatoryCaptureSource));
        OnPropertyChanged(nameof(CanInteract));
    }

    partial void OnIsMovableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
    }
}
