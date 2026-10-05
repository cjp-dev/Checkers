using System.Diagnostics;
using System.Text.Json;
using Checkers.Benchmark;
using Checkers.Core.AI;
using Checkers.Core.AI.Benchmark;
using Checkers.Core.Engine;
using Checkers.Core.Models;

if (args.Length > 0 && args[0].Equals("book", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = BookCommands.Run(args);
    return;
}

if (args.Any(a => a.Equals("eval-speed", StringComparison.OrdinalIgnoreCase)))
{
    await RunEvalSpeedBenchmarkAsync();
    return;
}

if (args.Any(a => a.Equals("eval-match", StringComparison.OrdinalIgnoreCase)))
{
    await RunEvalMatchAsync(args);
    return;
}

if (args.Any(a => a.Contains("phase", StringComparison.OrdinalIgnoreCase)))
{
    await RunPhaseBenchmarkAsync();
    return;
}

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

string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "brain");
if (!Directory.Exists(outputDir))
{
    outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "brain"));
}

if (Directory.Exists(outputDir))
{
    string fileName = isDeep ? "tt_deep_benchmark_results.json" : "tt_benchmark_results.json";
    string jsonPath = Path.Combine(outputDir, fileName);
    await File.WriteAllTextAsync(jsonPath, TranspositionBenchmarkRunner.ToJson(results));
    Console.WriteLine($"\nBenchmark JSON successfully saved to: {jsonPath}");
}

static async Task RunPhaseBenchmarkAsync()
{
    string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "brain");
    if (!Directory.Exists(outputDir))
    {
        outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "brain"));
    }

    string baselineJsonPath = Path.Combine(outputDir, "bitboard_deep_benchmark_results.json");
    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(baselineJsonPath));
    var positions = BenchmarkSuite.GetPositions();

    // JIT warmup
    {
        var warmEngine = new RuleEngine(CheckersVariant.International);
        var warmMoves = warmEngine.GetLegalMoves(positions[0].State);
        var warmTt = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
        var warmPlayer = new MinimaxPlayer(SearchLimits.FixedDepth(8), transpositionTable: warmTt, variant: CheckersVariant.International);
        await warmPlayer.GetMoveAsync(positions[0].State, warmMoves);
    }

    Console.WriteLine("====================================================================================");
    Console.WriteLine("  PART A: 40-Position Deep Fixed-Depth Benchmark (vs. Phase 0 Bitboard Baseline)");
    Console.WriteLine("====================================================================================");

    foreach (var variant in new[] { CheckersVariant.International, CheckersVariant.English })
    {
        string propName = variant == CheckersVariant.International ? "International" : "English";
        var arr = doc.RootElement.GetProperty(propName);
        var engine = new RuleEngine(variant);
        var tt1M = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
        var tt16M = TranspositionTable.FromEntries(TranspositionTable.MaxEntries);

        long totalNoTtNodes = 0, baseNoTtNodes = 0;
        long totalTt1MNodes = 0, baseTt1MNodes = 0;
        long totalTt16MNodes = 0;
        long totalTt1MCutoffs = 0, totalTt1MCollisions = 0;
        long totalTt16MCutoffs = 0, totalTt16MCollisions = 0;
        int identicalNoTt = 0, identicalTt1M = 0;

        var swNoTt = Stopwatch.StartNew();
        swNoTt.Stop();
        swNoTt.Reset();

        var swTt1M = Stopwatch.StartNew();
        swTt1M.Stop();
        swTt1M.Reset();

        var swTt16M = Stopwatch.StartNew();
        swTt16M.Stop();
        swTt16M.Reset();

        for (int i = 0; i < positions.Count; i++)
        {
            var item = arr[i];
            int depth = item.GetProperty("CalibratedDepth").GetInt32();
            long expectedNoTtNodes = item.GetProperty("BitboardNoTtNodes").GetInt64();
            long expectedTtNodes = item.GetProperty("BitboardTtNodes").GetInt64();
            baseNoTtNodes += expectedNoTtNodes;
            baseTt1MNodes += expectedTtNodes;

            var state = positions[i].State;
            var legalMoves = engine.GetLegalMoves(state);

            var noTtPlayer = new MinimaxPlayer(
                SearchLimits.FixedDepth(depth),
                useTranspositionTable: false,
                useQuiescence: true,
                variant: variant);

            swNoTt.Start();
            await noTtPlayer.GetMoveAsync(state, legalMoves);
            swNoTt.Stop();

            totalNoTtNodes += noTtPlayer.NodesEvaluated;
            if (noTtPlayer.NodesEvaluated == expectedNoTtNodes)
                identicalNoTt++;

            tt1M.Clear();
            var tt1MPlayer = new MinimaxPlayer(
                SearchLimits.FixedDepth(depth),
                transpositionTable: tt1M,
                useTranspositionTable: true,
                useQuiescence: true,
                variant: variant);

            swTt1M.Start();
            await tt1MPlayer.GetMoveAsync(state, legalMoves);
            swTt1M.Stop();

            totalTt1MNodes += tt1MPlayer.NodesEvaluated;
            totalTt1MCutoffs += tt1M.Cutoffs;
            totalTt1MCollisions += tt1M.Collisions;
            if (tt1MPlayer.NodesEvaluated == expectedTtNodes)
                identicalTt1M++;

            tt16M.Clear();
            var tt16MPlayer = new MinimaxPlayer(
                SearchLimits.FixedDepth(depth),
                transpositionTable: tt16M,
                useTranspositionTable: true,
                useQuiescence: true,
                variant: variant);

            swTt16M.Start();
            await tt16MPlayer.GetMoveAsync(state, legalMoves);
            swTt16M.Stop();

            totalTt16MNodes += tt16MPlayer.NodesEvaluated;
            totalTt16MCutoffs += tt16M.Cutoffs;
            totalTt16MCollisions += tt16M.Collisions;
        }

        double noTtSec = Math.Max(0.001, swNoTt.Elapsed.TotalSeconds);
        double tt1MSec = Math.Max(0.001, swTt1M.Elapsed.TotalSeconds);
        double tt16MSec = Math.Max(0.001, swTt16M.Elapsed.TotalSeconds);

        Console.WriteLine($"\n--- {variant} (All 40 Deep Positions, 7-14 Plies) ---");
        Console.WriteLine($"  No-TT      : {totalNoTtNodes,11:N0} nodes (vs {baseNoTtNodes,11:N0} base, {identicalNoTt}/40 exact) | {swNoTt.ElapsedMilliseconds,5} ms | {(totalNoTtNodes / 1_000_000.0) / noTtSec,6:F2}M nodes/s");
        Console.WriteLine($"  1M TT      : {totalTt1MNodes,11:N0} nodes (vs {baseTt1MNodes,11:N0} base, {identicalTt1M}/40 exact) | {swTt1M.ElapsedMilliseconds,5} ms | {(totalTt1MNodes / 1_000_000.0) / tt1MSec,6:F2}M nodes/s | Cutoffs: {totalTt1MCutoffs:N0}, Coll: {totalTt1MCollisions:N0}");
        Console.WriteLine($"  16M TT     : {totalTt16MNodes,11:N0} nodes                                   | {swTt16M.ElapsedMilliseconds,5} ms | {(totalTt16MNodes / 1_000_000.0) / tt16MSec,6:F2}M nodes/s | Cutoffs: {totalTt16MCutoffs:N0}, Coll: {totalTt16MCollisions:N0}");
    }

    Console.WriteLine("\n====================================================================================");
    Console.WriteLine("  PART B: 5-Position Timed Benchmark (5.0s Budget per Position, 16M TT, International)");
    Console.WriteLine("====================================================================================");

    int[] fiveIndices = [0, 12, 19, 31, 39]; // Pos #1 (Opening), #13 (Middlegame), #20 (Middlegame), #32 (Endgame), #40 (Blockade)
    var intlEngine = new RuleEngine(CheckersVariant.International);
    var timedTt = TranspositionTable.FromEntries(TranspositionTable.MaxEntries);

    foreach (int idx in fiveIndices)
    {
        var pos = positions[idx];
        var legalMoves = intlEngine.GetLegalMoves(pos.State);
        timedTt.Clear();

        var timedPlayer = new MinimaxPlayer(
            SearchLimits.TimePerMove(TimeSpan.FromSeconds(5)),
            transpositionTable: timedTt,
            useTranspositionTable: true,
            useQuiescence: true,
            variant: CheckersVariant.International);

        var sw = Stopwatch.StartNew();
        var bestMove = await timedPlayer.GetMoveAsync(pos.State, legalMoves);
        sw.Stop();

        double sec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
        double nps = (timedPlayer.NodesEvaluated / 1_000_000.0) / sec;
        Console.WriteLine(
            $"  Pos #{pos.Id,2} ({pos.Category,-16}): Depth = {timedPlayer.LastAnalysis?.Depth,-10} | " +
            $"Move = {bestMove.Notation,-8} | Score = {timedPlayer.LastAnalysis?.Value,-6} | " +
            $"Nodes = {timedPlayer.NodesEvaluated,11:N0} | Time = {sw.ElapsedMilliseconds,4} ms | NPS = {nps,6:F2}M/s");
    }
}

static async Task RunEvalSpeedBenchmarkAsync()
{
    string outputDir = GetDocsBrainDirectory();
    string baselineJsonPath = Path.Combine(outputDir, "bitboard_deep_benchmark_results.json");

    var report = await EvaluationMatchRunner.RunSpeedBenchmarkAsync(
        baselineJsonPath: baselineJsonPath,
        timedSecondsPerPosition: 5.0,
        log: Console.WriteLine);

    if (Directory.Exists(outputDir))
    {
        string jsonPath = Path.Combine(outputDir, "eval_speed_benchmark_results.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, options));
        Console.WriteLine($"\nEvaluation speed benchmark JSON saved to: {jsonPath}");
    }
}

static async Task RunEvalMatchAsync(string[] cliArgs)
{
    int timeMs = GetIntArg(cliArgs, "--time-ms", 1000);
    int workers = GetIntArg(cliArgs, "--workers", 4);
    int ballots = GetIntArg(cliArgs, "--ballots", 50);
    string variantArg = GetStringArg(cliArgs, "--variant", "All");

    var variants = new List<CheckersVariant>();
    if (variantArg.Equals("English", StringComparison.OrdinalIgnoreCase))
    {
        variants.Add(CheckersVariant.English);
    }
    else if (variantArg.Equals("International", StringComparison.OrdinalIgnoreCase))
    {
        variants.Add(CheckersVariant.International);
    }
    else
    {
        variants.Add(CheckersVariant.English);
        variants.Add(CheckersVariant.International);
    }

    var summaries = new List<VariantMatchSummary>();
    object consoleLock = new();

    foreach (var variant in variants)
    {
        Console.WriteLine("\n====================================================================================");
        Console.WriteLine($"  Bot-vs-Bot Evaluation Match: {variant} Checkers");
        Console.WriteLine($"  Ballots: {ballots} ({ballots * 2} games) | Time/Move: {timeMs} ms | Parallel Workers: {workers}");
        Console.WriteLine("====================================================================================");

        var summary = await EvaluationMatchRunner.RunMatchAsync(
            variant: variant,
            ballotCount: ballots,
            timePerMoveMs: timeMs,
            workers: workers,
            progressCallback: (done, total, game) =>
            {
                if (done % 5 == 0 || done == total)
                {
                    lock (consoleLock)
                    {
                        string side = game.NewEvalIsWhite ? "New=W" : "New=B";
                        string outcome = game.NewEvalPointsHalf switch
                        {
                            2 => "WIN ",
                            1 => "DRAW",
                            _ => "LOSS"
                        };
                        Console.WriteLine(
                            $"  [{variant,-13} {done,3}/{total,3}] Game #{game.GameId,3} (Ballot #{game.BallotId,2}, {side}): " +
                            $"{outcome} ({game.TerminationReason,-19}, {game.TotalPlies,3} plies, " +
                            $"New {game.AvgNewDepth,4:F1}d/{game.AvgNewNpsMillions,4:F1}M vs Leg {game.AvgLegacyDepth,4:F1}d/{game.AvgLegacyNpsMillions,4:F1}M)");
                    }
                }
            });

        summaries.Add(summary);
        Console.WriteLine();
        Console.WriteLine(EvaluationMatchRunner.FormatMatchSummaryMarkdown(summary));
    }

    string outputDir = GetDocsBrainDirectory();
    if (Directory.Exists(outputDir))
    {
        string suffix = variants.Count == 1 ? $"_{variants[0].ToString().ToLowerInvariant()}" : "";
        string jsonPath = Path.Combine(outputDir, $"eval_match_results{suffix}.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(summaries, options));
        Console.WriteLine($"Match results JSON saved to: {jsonPath}");
    }
}

static string GetDocsBrainDirectory()
{
    string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "brain");
    if (!Directory.Exists(outputDir))
    {
        outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "brain"));
    }
    return outputDir;
}

static int GetIntArg(string[] cliArgs, string flag, int defaultValue)
{
    for (int i = 0; i < cliArgs.Length - 1; i++)
    {
        if (cliArgs[i].Equals(flag, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(cliArgs[i + 1], out int parsed))
        {
            return parsed;
        }
    }
    return defaultValue;
}

static string GetStringArg(string[] cliArgs, string flag, string defaultValue)
{
    for (int i = 0; i < cliArgs.Length - 1; i++)
    {
        if (cliArgs[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
        {
            return cliArgs[i + 1];
        }
    }
    return defaultValue;
}

