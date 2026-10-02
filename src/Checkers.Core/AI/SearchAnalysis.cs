namespace Checkers.Core.AI;

/// <summary>
/// Captures search and evaluation metrics from an AI turn for analysis display.
/// </summary>
public sealed record SearchAnalysis
{
    public string Move { get; init; } = "-";
    public string Depth { get; init; } = "-";
    public string Value { get; init; } = "-";
    public string BestMove { get; init; } = "-";
    public string Nodes { get; init; } = "-";
    public string Evaluations { get; init; } = "-";
    public string Time { get; init; } = "-";
}
