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
    private string _value = "-";

    [ObservableProperty]
    private string _bestMove = "-";

    [ObservableProperty]
    private string _nodes = "-";

    [ObservableProperty]
    private string _evaluations = "-";

    [ObservableProperty]
    private string _time = "-";

    public void Update(SearchAnalysis analysis)
    {
        Move = analysis.Move;
        Depth = analysis.Depth;
        Value = analysis.Value;
        BestMove = analysis.BestMove;
        Nodes = analysis.Nodes;
        Evaluations = analysis.Evaluations;
        Time = analysis.Time;
        Updated?.Invoke();
    }

    public void Reset()
    {
        Move = "-";
        Depth = "-";
        Value = "-";
        BestMove = "-";
        Nodes = "-";
        Evaluations = "-";
        Time = "-";
        Updated?.Invoke();
    }
}
