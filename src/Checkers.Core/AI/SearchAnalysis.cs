namespace Checkers.Core.AI;

/// <summary>
/// Captures search and evaluation metrics from an AI turn for analysis display.
/// </summary>
public sealed record SearchAnalysis
{
    public string Move { get; init; } = "-";
    public string Depth { get; init; } = "-";
    public string Score { get; init; } = "-";
    public string BestMove { get; init; } = "-";
    public string Nodes { get; init; } = "-";
    public string Speed { get; init; } = "-";
    public string Time { get; init; } = "-";
    public bool FromBook { get; init; }

    // Backward-compatibility aliases
    public string Value { get => Score; init => Score = value; }
    public string Evaluations { get => Speed; init => Speed = value; }
}
