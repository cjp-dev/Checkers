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

    // With Default Transposition Table (1,048,576 entries)
    public long TtNodes { get; init; }
    public long TtTimeMs { get; init; }
    public string TtMove { get; init; } = string.Empty;
    public string TtScore { get; init; } = string.Empty;
    public long TtHits { get; init; }
    public long TtCutoffs { get; init; }
    public long TtCollisions { get; init; }

    // With Max Transposition Table (16,777,216 entries)
    public long MaxTtNodes { get; init; }
    public long MaxTtTimeMs { get; init; }
    public string MaxTtMove { get; init; } = string.Empty;
    public string MaxTtScore { get; init; } = string.Empty;
    public long MaxTtHits { get; init; }
    public long MaxTtCutoffs { get; init; }
    public long MaxTtCollisions { get; init; }

    // Comparative Metrics (Default TT vs Baseline)
    public double NodeReductionPercent => BaselineNodes > 0
        ? (double)(BaselineNodes - TtNodes) / BaselineNodes * 100.0
        : 0.0;

    public double SpeedupFactor => TtTimeMs > 0
        ? (double)BaselineTimeMs / TtTimeMs
        : 1.0;

    // Comparative Metrics (Max TT vs Baseline)
    public double MaxTtNodeReductionPercent => BaselineNodes > 0
        ? (double)(BaselineNodes - MaxTtNodes) / BaselineNodes * 100.0
        : 0.0;

    public double MaxTtSpeedupFactor => MaxTtTimeMs > 0
        ? (double)BaselineTimeMs / MaxTtTimeMs
        : 1.0;
}
