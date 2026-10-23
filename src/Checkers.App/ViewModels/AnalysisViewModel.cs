using Checkers.Core.AI;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Checkers.App.ViewModels;

public sealed partial class AnalysisViewModel : ObservableObject
{
    public event Action? Updated;

    [ObservableProperty]
    private string _move = "-";

    [ObservableProperty]
    private string _depth = "-";

    [ObservableProperty]
    private string _score = "-";

    [ObservableProperty]
    private string _bestMove = "-";

    [ObservableProperty]
    private string _nodes = "-";

    [ObservableProperty]
    private string _speed = "-";

    [ObservableProperty]
    private string _time = "-";

    // Backward-compatibility aliases
    public string Value => Score;
    public string Evaluations => Speed;

    public void Update(SearchAnalysis analysis)
    {
        Move = analysis.Move;
        Depth = analysis.Depth;
        Score = analysis.Score;
        BestMove = analysis.BestMove;
        Nodes = analysis.Nodes;
        Speed = analysis.Speed;
        Time = analysis.Time;
        Updated?.Invoke();
    }

    public void Reset()
    {
        Move = "-";
        Depth = "-";
        Score = "-";
        BestMove = "-";
        Nodes = "-";
        Speed = "-";
        Time = "-";
        Updated?.Invoke();
    }
}
