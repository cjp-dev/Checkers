using Checkers.Core.Models;

namespace Checkers.Core.AI.Benchmark;

/// <summary>
/// Captures the comparative performance and equivalence metrics between the classic
/// <see cref="BoardEngine.Array"/> engine and the 64-bit <see cref="BoardEngine.Bitboard"/> engine
/// on a single benchmark position for both No-TT and Default TT (1,048,576 entries) configurations.
/// </summary>
public sealed record EngineBenchmarkResult
{
    public required int PositionId { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required CheckersVariant Variant { get; init; }
    public required int CalibratedDepth { get; init; }

    // 1. No-TT Baseline (Array vs Bitboard)
    public required long ArrayNoTtNodes { get; init; }
    public required long ArrayNoTtTimeMs { get; init; }
    public required string ArrayNoTtMove { get; init; }
    public required string ArrayNoTtScore { get; init; }

    public required long BitboardNoTtNodes { get; init; }
    public required long BitboardNoTtTimeMs { get; init; }
    public required string BitboardNoTtMove { get; init; }
    public required string BitboardNoTtScore { get; init; }

    public double NoTtSpeedupFactor =>
        BitboardNoTtTimeMs > 0 ? Math.Round((double)ArrayNoTtTimeMs / BitboardNoTtTimeMs, 2) : 1.0;

    public bool NoTtIdentical =>
        ArrayNoTtNodes == BitboardNoTtNodes &&
        ArrayNoTtMove == BitboardNoTtMove &&
        ArrayNoTtScore == BitboardNoTtScore;

    // 2. Default TT 1,048,576 Entries (Array vs Bitboard)
    public required long ArrayTtNodes { get; init; }
    public required long ArrayTtTimeMs { get; init; }
    public required string ArrayTtMove { get; init; }
    public required string ArrayTtScore { get; init; }
    public required long ArrayTtCutoffs { get; init; }

    public required long BitboardTtNodes { get; init; }
    public required long BitboardTtTimeMs { get; init; }
    public required string BitboardTtMove { get; init; }
    public required string BitboardTtScore { get; init; }
    public required long BitboardTtCutoffs { get; init; }

    public double TtSpeedupFactor =>
        BitboardTtTimeMs > 0 ? Math.Round((double)ArrayTtTimeMs / BitboardTtTimeMs, 2) : 1.0;

    public bool TtIdentical =>
        ArrayTtNodes == BitboardTtNodes &&
        ArrayTtMove == BitboardTtMove &&
        ArrayTtScore == BitboardTtScore &&
        ArrayTtCutoffs == BitboardTtCutoffs;
}
