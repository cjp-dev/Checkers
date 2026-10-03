using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Checkers.Core.Engine;

namespace Checkers.Core.AI.Benchmark;

/// <summary>
/// Calibrates baseline search depth and executes the 40-position Transposition Table benchmark.
/// </summary>
public static class TranspositionBenchmarkRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Executes the full benchmark across all 40 positions with optional progress reporting.
    /// </summary>
    public static async Task<List<BenchmarkResult>> RunBenchmarkAsync(
        IReadOnlyList<BenchmarkPosition>? customPositions = null,
        Action<int, int, BenchmarkResult>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var positions = customPositions ?? BenchmarkSuite.GetPositions();
        var results = new List<BenchmarkResult>(positions.Count);
        var ruleEngine = new RuleEngine();
        var defaultTt = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
        var maxTt = TranspositionTable.FromEntries(TranspositionTable.MaxEntries);

        for (int i = 0; i < positions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pos = positions[i];

            var result = await BenchmarkSinglePositionAsync(pos, ruleEngine, defaultTt, maxTt, cancellationToken);
            results.Add(result);
            progressCallback?.Invoke(i + 1, positions.Count, result);
        }

        return results;
    }

    /// <summary>
    /// Benchmarks a single position by calibrating baseline depth (&lt;= 10s) and comparing with Default TT (1,048,576) and Max TT (16,777,216).
    /// </summary>
    public static async Task<BenchmarkResult> BenchmarkSinglePositionAsync(
        BenchmarkPosition pos,
        IRuleEngine ruleEngine,
        TranspositionTable? defaultTt = null,
        TranspositionTable? maxTt = null,
        CancellationToken cancellationToken = default)
    {
        var legalMoves = ruleEngine.GetLegalMoves(pos.State);
        if (legalMoves.Count == 0)
        {
            throw new InvalidOperationException($"Position {pos.Id} has no legal moves.");
        }

        // 1. Calibrate baseline search depth (<= 10 seconds)
        int calibratedDepth = CalibrateDepth(pos.State, legalMoves);

        // 2. Run Baseline (Without Transposition Table)
        var baselinePlayer = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(calibratedDepth),
            useTranspositionTable: false,
            useQuiescence: true);

        var baselineSw = Stopwatch.StartNew();
        var baselineMove = await baselinePlayer.GetMoveAsync(pos.State, legalMoves, cancellationToken);
        baselineSw.Stop();
        long baselineTimeMs = Math.Max(1, baselineSw.ElapsedMilliseconds);
        long baselineNodes = baselinePlayer.NodesEvaluated;
        string baselineScore = baselinePlayer.LastAnalysis?.Value ?? "-";

        // 3. Run with Default Transposition Table (1,048,576 entries)
        var tt = defaultTt ?? TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
        tt.Clear();
        var ttPlayer = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(calibratedDepth),
            transpositionTable: tt,
            useTranspositionTable: true,
            useQuiescence: true);

        var ttSw = Stopwatch.StartNew();
        var ttMove = await ttPlayer.GetMoveAsync(pos.State, legalMoves, cancellationToken);
        ttSw.Stop();
        long ttTimeMs = Math.Max(1, ttSw.ElapsedMilliseconds);
        long ttNodes = ttPlayer.NodesEvaluated;
        string ttScore = ttPlayer.LastAnalysis?.Value ?? "-";
        long ttHits = tt.Hits;
        long ttCutoffs = tt.Cutoffs;
        long ttCollisions = tt.Collisions;

        // 4. Run with Max Transposition Table (16,777,216 entries)
        var largeTt = maxTt ?? TranspositionTable.FromEntries(TranspositionTable.MaxEntries);
        largeTt.Clear();
        var maxTtPlayer = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(calibratedDepth),
            transpositionTable: largeTt,
            useTranspositionTable: true,
            useQuiescence: true);

        var maxTtSw = Stopwatch.StartNew();
        var maxTtMove = await maxTtPlayer.GetMoveAsync(pos.State, legalMoves, cancellationToken);
        maxTtSw.Stop();
        long maxTtTimeMs = Math.Max(1, maxTtSw.ElapsedMilliseconds);
        long maxTtNodes = maxTtPlayer.NodesEvaluated;
        string maxTtScore = maxTtPlayer.LastAnalysis?.Value ?? "-";
        long maxTtHits = largeTt.Hits;
        long maxTtCutoffs = largeTt.Cutoffs;
        long maxTtCollisions = largeTt.Collisions;

        return new BenchmarkResult
        {
            PositionId = pos.Id,
            Category = pos.Category,
            Description = pos.Description,
            CalibratedDepth = calibratedDepth,

            BaselineNodes = baselineNodes,
            BaselineTimeMs = baselineTimeMs,
            BaselineMove = baselineMove.Notation,
            BaselineScore = baselineScore,

            TtNodes = ttNodes,
            TtTimeMs = ttTimeMs,
            TtMove = ttMove.Notation,
            TtScore = ttScore,
            TtHits = ttHits,
            TtCutoffs = ttCutoffs,
            TtCollisions = ttCollisions,

            MaxTtNodes = maxTtNodes,
            MaxTtTimeMs = maxTtTimeMs,
            MaxTtMove = maxTtMove.Notation,
            MaxTtScore = maxTtScore,
            MaxTtHits = maxTtHits,
            MaxTtCutoffs = maxTtCutoffs,
            MaxTtCollisions = maxTtCollisions
        };
    }

    /// <summary>
    /// Calibrates search depth so baseline completes in &lt;= 10 seconds (targeting ~50 ms to 2 seconds).
    /// </summary>
    private static int CalibrateDepth(Models.BoardState state, IReadOnlyList<Models.Move> legalMoves)
    {
        int depth = 5;

        while (depth < 9)
        {
            var testPlayer = new MinimaxPlayer(
                limits: SearchLimits.FixedDepth(depth),
                useTranspositionTable: false,
                useQuiescence: true);

            var sw = Stopwatch.StartNew();
            testPlayer.GetMoveAsync(state, legalMoves).AsTask().GetAwaiter().GetResult();
            sw.Stop();
            long elapsedMs = sw.ElapsedMilliseconds;

            if (elapsedMs >= 600 ||
                (depth >= 7 && elapsedMs >= 60) ||
                (depth >= 8 && elapsedMs >= 15))
            {
                break;
            }

            depth++;
        }

        return depth;
    }

    /// <summary>
    /// Formats the benchmark results (Baseline vs Default TT) as a clean Markdown table.
    /// </summary>
    public static string FormatMarkdownTable(IReadOnlyList<BenchmarkResult> results)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("| # | Category | Depth | Baseline Nodes | TT Nodes | Node Reduction | Baseline Time | TT Time | Speedup | TT Cutoffs |");
        sb.AppendLine("|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|");

        long totalBaseNodes = 0;
        long totalTtNodes = 0;
        long totalBaseTime = 0;
        long totalTtTime = 0;
        long totalCutoffs = 0;

        foreach (var r in results)
        {
            totalBaseNodes += r.BaselineNodes;
            totalTtNodes += r.TtNodes;
            totalBaseTime += r.BaselineTimeMs;
            totalTtTime += r.TtTimeMs;
            totalCutoffs += r.TtCutoffs;

            sb.AppendLine(string.Create(inv,
                $"| {r.PositionId} | {r.Category} | {r.CalibratedDepth} plies | {r.BaselineNodes:N0} | {r.TtNodes:N0} | **{r.NodeReductionPercent:F1}%** | {r.BaselineTimeMs:N0} ms | {r.TtTimeMs:N0} ms | **{r.SpeedupFactor:F2}x** | {r.TtCutoffs:N0} |"));
        }

        double overallReduction = totalBaseNodes > 0 ? (double)(totalBaseNodes - totalTtNodes) / totalBaseNodes * 100.0 : 0;
        double overallSpeedup = totalTtTime > 0 ? (double)totalBaseTime / totalTtTime : 1.0;

        sb.AppendLine("|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|");
        sb.AppendLine(string.Create(inv,
            $"| **Total** | **All 40** | **7–9 plies** | **{totalBaseNodes:N0}** | **{totalTtNodes:N0}** | **{overallReduction:F1}%** | **{totalBaseTime:N0} ms** | **{totalTtTime:N0} ms** | **{overallSpeedup:F2}x** | **{totalCutoffs:N0}** |"));

        return sb.ToString();
    }

    /// <summary>
    /// Formats a comparative Markdown table comparing Default TT (1,048,576 entries) vs Max TT (16,777,216 entries).
    /// </summary>
    public static string FormatSizeComparisonMarkdownTable(IReadOnlyList<BenchmarkResult> results)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("| # | Category | Depth | Baseline Nodes | Default TT Nodes (1M) | Max TT Nodes (16M) | Default Time | Max Time | Default Collisions | Max Collisions |");
        sb.AppendLine("|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|");

        long totalBaseNodes = 0;
        long totalDefNodes = 0;
        long totalMaxNodes = 0;
        long totalDefTime = 0;
        long totalMaxTime = 0;
        long totalDefCollisions = 0;
        long totalMaxCollisions = 0;

        foreach (var r in results)
        {
            totalBaseNodes += r.BaselineNodes;
            totalDefNodes += r.TtNodes;
            totalMaxNodes += r.MaxTtNodes;
            totalDefTime += r.TtTimeMs;
            totalMaxTime += r.MaxTtTimeMs;
            totalDefCollisions += r.TtCollisions;
            totalMaxCollisions += r.MaxTtCollisions;

            sb.AppendLine(string.Create(inv,
                $"| {r.PositionId} | {r.Category} | {r.CalibratedDepth} plies | {r.BaselineNodes:N0} | {r.TtNodes:N0} | {r.MaxTtNodes:N0} | {r.TtTimeMs:N0} ms | {r.MaxTtTimeMs:N0} ms | {r.TtCollisions:N0} | {r.MaxTtCollisions:N0} |"));
        }

        sb.AppendLine("|---|:---:|:---:|---:|---:|---:|---:|---:|---:|---:|");
        sb.AppendLine(string.Create(inv,
            $"| **Total** | **All 40** | **7–9 plies** | **{totalBaseNodes:N0}** | **{totalDefNodes:N0}** | **{totalMaxNodes:N0}** | **{totalDefTime:N0} ms** | **{totalMaxTime:N0} ms** | **{totalDefCollisions:N0}** | **{totalMaxCollisions:N0}** |"));

        return sb.ToString();
    }

    public static string ToJson(IReadOnlyList<BenchmarkResult> results) =>
        JsonSerializer.Serialize(results, JsonOptions);
}
