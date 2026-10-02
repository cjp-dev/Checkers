namespace Checkers.Core.AI.Benchmark;

/// <summary>
/// Detailed comparative results for a single benchmark position.
/// </summary>
public sealed record BenchmarkResult
{
    public int PositionId { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int CalibratedDepth { get; init; }

    // Baseline (Without TT)
    public long BaselineNodes { get; init; }
    public long BaselineTimeMs { get; init; }
    public string BaselineMove { get; init; } = string.Empty;
    public string BaselineScore { get; init; } = string.Empty;

    // With Transposition Table
    public long TtNodes { get; init; }
    public long TtTimeMs { get; init; }
    public string TtMove { get; init; } = string.Empty;
    public string TtScore { get; init; } = string.Empty;
    public long TtHits { get; init; }
    public long TtCutoffs { get; init; }
    public long TtCollisions { get; init; }

    // Comparative Metrics
    public double NodeReductionPercent => BaselineNodes > 0
        ? (double)(BaselineNodes - TtNodes) / BaselineNodes * 100.0
        : 0.0;

    public double SpeedupFactor => TtTimeMs > 0
        ? (double)BaselineTimeMs / TtTimeMs
        : 1.0;
}
