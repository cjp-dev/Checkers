using Checkers.Core.Models;

namespace Checkers.Core.AI.Benchmark;

/// <summary>
/// Represents a benchmark board position with metadata.
/// </summary>
public sealed record BenchmarkPosition(
    int Id,
    string Category,
    string Description,
    BoardState State);
