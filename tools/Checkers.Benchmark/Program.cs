using Checkers.Core.AI.Benchmark;
using Checkers.Core.Models;

bool isDeep = args.Any(a => a.Equals("--deep", StringComparison.OrdinalIgnoreCase));
bool isEngineComparison = args.Any(a => a.Equals("--engines", StringComparison.OrdinalIgnoreCase));

string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "brain");
if (!Directory.Exists(outputDir))
{
    outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "brain"));
}

var positions = BenchmarkSuite.GetPositions();

if (isEngineComparison)
{
    Console.WriteLine("=========================================================================");
    Console.WriteLine($"  Checkers Bitboard vs Array Engine Benchmark (40 Positions){(isDeep ? " [DEEP ~5s/pos]" : "")}");
    Console.WriteLine("  Variants: International (Flying Kings) & English Checkers (1-Step Kings)");
    Console.WriteLine("  Modes:    No-TT Baseline & Default TT (1,048,576 entries)               ");
    Console.WriteLine("=========================================================================\n");

    Console.WriteLine($"--- [1/2] Running INTERNATIONAL DRAUGHTS (Flying Kings) across {positions.Count} positions ---\n");
    var intlResults = await EngineBenchmarkRunner.RunVariantBenchmarkAsync(
        CheckersVariant.International,
        positions,
        progressCallback: (current, total, r) =>
        {
            Console.WriteLine(
                $"[Intl {current:D2}/{total:D2}] {r.Category,-16} d={r.CalibratedDepth,2}: " +
                $"No-TT Arr {r.ArrayNoTtTimeMs,4}ms vs Bit {r.BitboardNoTtTimeMs,4}ms ({r.NoTtSpeedupFactor,5:F2}x) | " +
                $"1M TT Arr {r.ArrayTtTimeMs,4}ms vs Bit {r.BitboardTtTimeMs,4}ms ({r.TtSpeedupFactor,5:F2}x) | " +
                $"Eq={(r.NoTtIdentical && r.TtIdentical ? "OK" : "FAIL")}");
        },
        deepCalibration: isDeep);

    Console.WriteLine($"\n--- [2/2] Running ENGLISH CHECKERS (1-Step Kings) across {positions.Count} positions ---\n");
    var engResults = await EngineBenchmarkRunner.RunVariantBenchmarkAsync(
        CheckersVariant.English,
        positions,
        progressCallback: (current, total, r) =>
        {
            Console.WriteLine(
                $"[Eng  {current:D2}/{total:D2}] {r.Category,-16} d={r.CalibratedDepth,2}: " +
                $"No-TT Arr {r.ArrayNoTtTimeMs,4}ms vs Bit {r.BitboardNoTtTimeMs,4}ms ({r.NoTtSpeedupFactor,5:F2}x) | " +
                $"1M TT Arr {r.ArrayTtTimeMs,4}ms vs Bit {r.BitboardTtTimeMs,4}ms ({r.TtSpeedupFactor,5:F2}x) | " +
                $"Eq={(r.NoTtIdentical && r.TtIdentical ? "OK" : "FAIL")}");
        },
        deepCalibration: isDeep);

    Console.WriteLine("\n=========================================================================");
    Console.WriteLine("   TABLE 1A: INTERNATIONAL DRAUGHTS â€” NO-TT BASELINE (ARRAY vs BITBOARD) ");
    Console.WriteLine("=========================================================================\n");
    Console.WriteLine(EngineBenchmarkRunner.FormatNoTtMarkdownTable(intlResults));

    Console.WriteLine("\n=========================================================================");
    Console.WriteLine("   TABLE 1B: INTERNATIONAL DRAUGHTS â€” DEFAULT TT 1M (ARRAY vs BITBOARD)  ");
    Console.WriteLine("=========================================================================\n");
    Console.WriteLine(EngineBenchmarkRunner.FormatTtMarkdownTable(intlResults));

    Console.WriteLine("\n=========================================================================");
    Console.WriteLine("   TABLE 2A: ENGLISH CHECKERS â€” NO-TT BASELINE (ARRAY vs BITBOARD)       ");
    Console.WriteLine("=========================================================================\n");
    Console.WriteLine(EngineBenchmarkRunner.FormatNoTtMarkdownTable(engResults));

    Console.WriteLine("\n=========================================================================");
    Console.WriteLine("   TABLE 2B: ENGLISH CHECKERS â€” DEFAULT TT 1M (ARRAY vs BITBOARD)        ");
    Console.WriteLine("=========================================================================\n");
    Console.WriteLine(EngineBenchmarkRunner.FormatTtMarkdownTable(engResults));

    if (Directory.Exists(outputDir))
    {
        string fileName = isDeep ? "bitboard_deep_benchmark_results.json" : "bitboard_benchmark_results.json";
        string jsonPath = Path.Combine(outputDir, fileName);
        string json = EngineBenchmarkRunner.ToJson(new
        {
            International = intlResults,
            English = engResults
        });
        await File.WriteAllTextAsync(jsonPath, json);
        Console.WriteLine($"\nEngine benchmark JSON saved to: {jsonPath}");
    }
    return;
}

Console.WriteLine("=========================================================================");
Console.WriteLine($"  Checkers Transposition Table Empirical Benchmark (40 Positions){(isDeep ? " [DEEP ~5s/pos]" : "")}");
Console.WriteLine("  Comparing: Baseline vs Default TT (1,048,576) vs Max TT (16,777,216)   ");
Console.WriteLine("=========================================================================");
Console.WriteLine(isDeep
    ? "Methodology: Deep baseline search depth calibrated targeting ~3-7s per position (<= 8.5s ceiling)."
    : "Methodology: Baseline search depth calibrated so search completes in <= 10s.");
Console.WriteLine("Then each position is tested at identical depth with Default TT (2^20) and Max TT (2^24).");
Console.WriteLine();

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

if (Directory.Exists(outputDir))
{
    string fileName = isDeep ? "tt_deep_benchmark_results.json" : "tt_benchmark_results.json";
    string jsonPath = Path.Combine(outputDir, fileName);
    await File.WriteAllTextAsync(jsonPath, TranspositionBenchmarkRunner.ToJson(results));
    Console.WriteLine($"\nBenchmark JSON successfully saved to: {jsonPath}");
}
