using Checkers.Core.AI.Benchmark;

Console.WriteLine("===================================================================");
Console.WriteLine("    Checkers Transposition Table Empirical Benchmark (40 Positions) ");
Console.WriteLine("===================================================================");
Console.WriteLine("Methodology: Baseline search depth calibrated so search completes in <= 10s.");
Console.WriteLine("Then each position is tested with Transposition Table enabled at identical depth.");
Console.WriteLine();

var positions = BenchmarkSuite.GetPositions();
Console.WriteLine($"Generated {positions.Count} test positions across Openings, Middlegames, Endgames, and Blockades.");
Console.WriteLine("Starting benchmark runs...\n");

var results = await TranspositionBenchmarkRunner.RunBenchmarkAsync(
    positions,
    progressCallback: (current, total, result) =>
    {
        Console.WriteLine(
            $"[{current:D2}/{total:D2}] {result.Category,-12} Depth {result.CalibratedDepth} plies: " +
            $"Baseline {result.BaselineNodes,7:N0} nodes ({result.BaselineTimeMs,4} ms) -> " +
            $"TT {result.TtNodes,7:N0} nodes ({result.TtTimeMs,4} ms) " +
            $"[{result.NodeReductionPercent,5:F1}% node reduction, {result.SpeedupFactor,4:F2}x speedup, {result.TtCutoffs,5:N0} cutoffs]");
    });

Console.WriteLine("\n===================================================================");
Console.WriteLine("                     BENCHMARK COMPLETE                            ");
Console.WriteLine("===================================================================\n");

string markdown = TranspositionBenchmarkRunner.FormatMarkdownTable(results);
Console.WriteLine(markdown);

string json = TranspositionBenchmarkRunner.ToJson(results);
string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "brain");
if (!Directory.Exists(outputDir))
{
    // Try navigating from tools/Checkers.Benchmark
    outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "brain"));
}

if (Directory.Exists(outputDir))
{
    string jsonPath = Path.Combine(outputDir, "tt_benchmark_results.json");
    await File.WriteAllTextAsync(jsonPath, json);
    Console.WriteLine($"\nBenchmark JSON successfully saved to: {jsonPath}");
}
