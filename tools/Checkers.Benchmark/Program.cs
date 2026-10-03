using Checkers.Core.AI.Benchmark;

bool isDeep = args.Any(a => a.Equals("--deep", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("=========================================================================");
Console.WriteLine($"  Checkers Transposition Table Empirical Benchmark (40 Positions){(isDeep ? " [DEEP ~5s/pos]" : "")}");
Console.WriteLine("  Comparing: Baseline vs Default TT (1,048,576) vs Max TT (16,777,216)   ");
Console.WriteLine("=========================================================================");
Console.WriteLine(isDeep
    ? "Methodology: Deep baseline search depth calibrated targeting ~3-7s per position (<= 8.5s ceiling)."
    : "Methodology: Baseline search depth calibrated so search completes in <= 10s.");
Console.WriteLine("Then each position is tested at identical depth with Default TT (2^20) and Max TT (2^24).");
Console.WriteLine();

var positions = BenchmarkSuite.GetPositions();
Console.WriteLine($"Generated {positions.Count} test positions across Openings, Middlegames, Endgames, and Blockades.");
Console.WriteLine("Starting benchmark runs...\n");

var results = await TranspositionBenchmarkRunner.RunBenchmarkAsync(
    positions,
    progressCallback: (current, total, result) =>
    {
        Console.WriteLine(
            $"[{current:D2}/{total:D2}] {result.Category,-16} Depth {result.CalibratedDepth,2}: " +
            $"Base {result.BaselineNodes,9:N0} ({result.BaselineTimeMs,4}ms) | " +
            $"1M TT {result.TtNodes,7:N0} ({result.TtTimeMs,4}ms, {result.TtCollisions,4} coll) | " +
            $"16M TT {result.MaxTtNodes,7:N0} ({result.MaxTtTimeMs,4}ms, {result.MaxTtCollisions,3} coll)");
    },
    deepCalibration: isDeep);

Console.WriteLine("\n=========================================================================");
Console.WriteLine("          TABLE 1: BASELINE vs DEFAULT TT (1,048,576 ENTRIES)            ");
Console.WriteLine("=========================================================================\n");

string markdown = TranspositionBenchmarkRunner.FormatMarkdownTable(results);
Console.WriteLine(markdown);

Console.WriteLine("\n=========================================================================");
Console.WriteLine("   TABLE 2: DEFAULT TT (1,048,576) vs MAX TT (16,777,216 ENTRIES)        ");
Console.WriteLine("=========================================================================\n");

string sizeComparisonMarkdown = TranspositionBenchmarkRunner.FormatSizeComparisonMarkdownTable(results);
Console.WriteLine(sizeComparisonMarkdown);

string json = TranspositionBenchmarkRunner.ToJson(results);
string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "brain");
if (!Directory.Exists(outputDir))
{
    outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "brain"));
}

if (Directory.Exists(outputDir))
{
    string fileName = isDeep ? "tt_deep_benchmark_results.json" : "tt_benchmark_results.json";
    string jsonPath = Path.Combine(outputDir, fileName);
    await File.WriteAllTextAsync(jsonPath, json);
    Console.WriteLine($"\nBenchmark JSON successfully saved to: {jsonPath}");
}
