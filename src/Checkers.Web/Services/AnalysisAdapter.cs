using Checkers.App.ViewModels;
using GameShell;

namespace Checkers.Web.Services;

/// <summary>Shows the platform-neutral analysis view model in GameShell's analysis panel.</summary>
public sealed class AnalysisAdapter(AnalysisViewModel analysis) : IAnalysisView
{
    public string Move => analysis.Move;
    public string Depth => analysis.Depth;
    public string Score => analysis.Score;
    public string BestMove => analysis.BestMove;
    public string Nodes => analysis.Nodes;
    public string Speed => analysis.Speed;
    public string Time => analysis.Time;

    public event Action? Updated
    {
        add => analysis.Updated += value;
        remove => analysis.Updated -= value;
    }
}
