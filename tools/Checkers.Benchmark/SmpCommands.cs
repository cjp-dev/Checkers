using System.Diagnostics;
using System.Text.Json;
using Checkers.Core.AI;
using Checkers.Core.AI.Benchmark;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Benchmark;

/// <summary>
/// Lazy SMP tooling: the fixed-depth search signature (proves the 1-thread search is unchanged),
/// the time-to-depth scaling benchmark and the thread-count match.
/// </summary>
internal static class SmpCommands
{
    private sealed record SignatureEntry(string Variant, int PositionId, int Depth, string Table, long Nodes, string Move, string Score);

    /// <summary>
    /// <c>signature [--depth-plus n] [--out file] [--compare file]</c>: searches the 40 benchmark positions of both variants at their
    /// calibrated depth (plus <c>--depth-plus</c>), with no TT and with a fresh 1M TT, and records nodes, move and score.
    /// </summary>
    public static async Task<int> RunSignatureAsync(string[] args)
    {
        string? outPath = GetStringArg(args, "--out", null);
        string? comparePath = GetStringArg(args, "--compare", null);
        int depthPlus = GetIntArg(args, "--depth-plus", 0);

        var depths = LoadCalibratedDepths();
        var positions = BenchmarkSuite.GetPositions();
        var entries = new List<SignatureEntry>();
        var sw = Stopwatch.StartNew();

        foreach (var variant in new[] { CheckersVariant.International, CheckersVariant.English })
        {
            var engine = new RuleEngine(variant);
            var tt = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
            for (int i = 0; i < positions.Count; i++)
            {
                int depth = depths[variant][i] + depthPlus;
                var state = positions[i].State;
                var legalMoves = engine.GetLegalMoves(state);

                foreach (bool useTt in new[] { false, true })
                {
                    tt.Clear();
                    var player = new MinimaxPlayer(
                        SearchLimits.FixedDepth(depth),
                        transpositionTable: useTt ? tt : null,
                        useTranspositionTable: useTt,
                        variant: variant);
                    Move move = await player.GetMoveAsync(state, legalMoves);
                    entries.Add(new SignatureEntry(
                        variant.ToString(), positions[i].Id, depth, useTt ? "TT1M" : "NoTT",
                        player.NodesEvaluated, move.Notation, player.LastAnalysis?.Value ?? ""));
                }
            }
        }

        long totalNodes = entries.Sum(e => e.Nodes);
        Console.WriteLine($"Signature: {entries.Count} searches, {totalNodes:N0} nodes, {sw.ElapsedMilliseconds:N0} ms");

        var json = new JsonSerializerOptions { WriteIndented = true };
        if (outPath != null)
        {
            await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(entries, json));
            Console.WriteLine($"Saved to {outPath}");
        }

        if (comparePath == null)
        {
            return 0;
        }

        var expected = JsonSerializer.Deserialize<List<SignatureEntry>>(await File.ReadAllTextAsync(comparePath))!;
        int mismatches = 0;
        for (int i = 0; i < Math.Max(expected.Count, entries.Count); i++)
        {
            SignatureEntry? e = i < expected.Count ? expected[i] : null;
            SignatureEntry? a = i < entries.Count ? entries[i] : null;
            if (e != a)
            {
                mismatches++;
                Console.WriteLine($"  MISMATCH expected {e} | actual {a}");
            }
        }

        Console.WriteLine(mismatches == 0
            ? $"Signature IDENTICAL ({entries.Count}/{expected.Count})"
            : $"Signature DIFFERS: {mismatches} of {expected.Count} searches");
        return mismatches == 0 ? 0 : 1;
    }

    /// <summary>
    /// <c>smp-bench [--threads 1,2,4,8] [--variant International] [--depth-plus 2] [--passes 3] [--tt-entries n]</c>:
    /// time to reach the calibrated depth on the 40 benchmark positions for each thread count.
    /// Passes are interleaved over the thread counts and the minimum time per position is kept.
    /// </summary>
    public static async Task<int> RunSmpBenchAsync(string[] args)
    {
        int[] threadCounts = GetStringArg(args, "--threads", "1,2,4,8")!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();
        CheckersVariant variant = Enum.Parse<CheckersVariant>(GetStringArg(args, "--variant", "International")!, ignoreCase: true);
        int depthPlus = GetIntArg(args, "--depth-plus", 2);
        int passes = GetIntArg(args, "--passes", 3);
        int ttEntries = GetIntArg(args, "--tt-entries", TranspositionTable.DefaultEntries);

        int[] depths = LoadCalibratedDepths()[variant];
        var positions = BenchmarkSuite.GetPositions();
        var engine = new RuleEngine(variant);
        var tt = TranspositionTable.FromEntries(ttEntries);

        // [thread index][position] -> best time / nodes of that run
        var bestMs = new double[threadCounts.Length][];
        var nodesAtBest = new long[threadCounts.Length][];
        var moveChanges = new int[threadCounts.Length];
        for (int t = 0; t < threadCounts.Length; t++)
        {
            bestMs[t] = Enumerable.Repeat(double.MaxValue, positions.Count).ToArray();
            nodesAtBest[t] = new long[positions.Count];
        }
        var singleThreadMoves = new string?[positions.Count];

        Console.WriteLine($"smp-bench: {variant}, 40 positions at calibrated depth +{depthPlus}, threads {string.Join(",", threadCounts)}, {passes} passes, TT {ttEntries:N0}");

        for (int pass = 1; pass <= passes; pass++)
        {
            for (int t = 0; t < threadCounts.Length; t++)
            {
                double passMs = 0;
                for (int i = 0; i < positions.Count; i++)
                {
                    var state = positions[i].State;
                    var legalMoves = engine.GetLegalMoves(state);
                    tt.Clear();
                    var player = new MinimaxPlayer(SearchLimits.FixedDepth(depths[i] + depthPlus), transpositionTable: tt, variant: variant)
                    {
                        Threads = threadCounts[t]
                    };

                    var sw = Stopwatch.StartNew();
                    Move move = await player.GetMoveAsync(state, legalMoves);
                    sw.Stop();

                    passMs += sw.Elapsed.TotalMilliseconds;
                    if (sw.Elapsed.TotalMilliseconds < bestMs[t][i])
                    {
                        bestMs[t][i] = sw.Elapsed.TotalMilliseconds;
                        nodesAtBest[t][i] = player.NodesEvaluated;
                    }

                    if (threadCounts[t] == 1)
                    {
                        singleThreadMoves[i] = move.Notation;
                    }
                    else if (pass == passes && singleThreadMoves[i] != null && singleThreadMoves[i] != move.Notation)
                    {
                        moveChanges[t]++;
                    }
                }

                Console.WriteLine($"  pass {pass} threads {threadCounts[t],2}: {passMs,9:N0} ms");
            }
        }

        double baseMs = bestMs[0].Sum();
        long baseNodes = nodesAtBest[0].Sum();
        Console.WriteLine();
        Console.WriteLine($"| Threads | Time to depth (ms) | Speedup | Nodes | Nodes vs {threadCounts[0]}T | NPS (M) | Moves differing from 1T |");
        Console.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        for (int t = 0; t < threadCounts.Length; t++)
        {
            double ms = bestMs[t].Sum();
            long nodes = nodesAtBest[t].Sum();
            string changes = threadCounts[t] == 1 || !threadCounts.Contains(1) ? "-" : $"{moveChanges[t]}/40";
            Console.WriteLine(
                $"| {threadCounts[t]} | {ms:N0} | {baseMs / ms:F2}x | {nodes:N0} | {(double)nodes / baseNodes:F2}x | {nodes / 1000.0 / ms:F2} | {changes} |");
        }

        return 0;
    }

    /// <summary>
    /// <c>smp-match [--variant International] [--threads-a 1] [--threads-b 4] [--time-ms 500] [--ballots 200] [--workers n]</c>:
    /// side B (the candidate, reported as "new") against side A (the baseline). Both use the same build and
    /// evaluation; only the thread count differs. Games run in parallel without oversubscribing the CPU.
    /// </summary>
    public static async Task<int> RunSmpMatchAsync(string[] args)
    {
        CheckersVariant variant = Enum.Parse<CheckersVariant>(GetStringArg(args, "--variant", "International")!, ignoreCase: true);
        int threadsA = GetIntArg(args, "--threads-a", 1);
        int threadsB = GetIntArg(args, "--threads-b", 4);
        int timeMs = GetIntArg(args, "--time-ms", 500);
        int ballots = GetIntArg(args, "--ballots", 200);
        int workers = GetIntArg(args, "--workers", Math.Max(1, Environment.ProcessorCount / Math.Max(threadsA, threadsB)));

        string nameA = $"{threadsA}T";
        string nameB = $"{threadsB}T";
        Console.WriteLine($"smp-match: {variant}, {nameB} (new) vs {nameA} (baseline), {ballots} ballots ({ballots * 2} games), {timeMs} ms/move, {workers} games at a time");

        object consoleLock = new();
        int points = 0;
        var summary = await EvaluationMatchRunner.RunMatchAsync(
            variant,
            createNewPlayer: (ruleEngine, limits) =>
                EvaluationMatchRunner.CreateMatchPlayer(ruleEngine, limits, new EvaluationFunction(variant), threadsB),
            createLegacyPlayer: (ruleEngine, limits) =>
                EvaluationMatchRunner.CreateMatchPlayer(ruleEngine, limits, new EvaluationFunction(variant), threadsA),
            ballots,
            timeMs,
            workers,
            (done, total, game) =>
            {
                lock (consoleLock)
                {
                    points += game.NewEvalPointsHalf;
                    if (done % 20 == 0 || done == total)
                    {
                        Console.WriteLine(
                            $"  [{done,3}/{total}] {nameB} score {points / 2.0:F1}/{done} ({50.0 * points / done:F1}%) | " +
                            $"depth {game.AvgNewDepth:F1} vs {game.AvgLegacyDepth:F1}, {game.AvgNewNpsMillions:F1}M vs {game.AvgLegacyNpsMillions:F1}M nps");
                    }
                }
            });

        string markdown = EvaluationMatchRunner.FormatMatchSummaryMarkdown(summary, nameB, $"Threads = {threadsB}", nameA, $"Threads = {threadsA}");
        Console.WriteLine();
        Console.WriteLine(markdown);

        double lowerScore = SummaryLowerBoundPercent(summary);
        Console.WriteLine($"Score {summary.ScorePercentage:F1}% (95% lower bound {lowerScore:F1}%), Elo {summary.EloDifference:+0.0;-0.0} ± {summary.EloError95:F1}");

        string dir = Path.Combine(GetDocsBrainDirectory(), "..", "..", "Plans", "lazy-smp-results");
        Directory.CreateDirectory(dir);
        string baseName = $"smp_match_{variant.ToString().ToLowerInvariant()}_{threadsB}t_vs_{threadsA}t_{timeMs}ms";
        var json = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(dir, baseName + ".json"), JsonSerializer.Serialize(summary, json));
        await File.WriteAllTextAsync(Path.Combine(dir, baseName + ".md"), markdown);
        Console.WriteLine($"Saved to {Path.GetFullPath(Path.Combine(dir, baseName))}.json/.md");
        return 0;
    }

    private static double SummaryLowerBoundPercent(VariantMatchSummary s)
    {
        int n = s.TotalGames;
        double mu = (s.NewEvalWins + 0.5 * s.Draws) / n;
        double variance = (s.NewEvalWins * Math.Pow(1.0 - mu, 2) + s.Draws * Math.Pow(0.5 - mu, 2) + s.LegacyEvalWins * Math.Pow(mu, 2)) / Math.Max(1, n - 1);
        return 100.0 * (mu - 1.96 * Math.Sqrt(variance / n));
    }

    private static Dictionary<CheckersVariant, int[]> LoadCalibratedDepths()
    {
        string path = Path.Combine(GetDocsBrainDirectory(), "bitboard_deep_benchmark_results.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<CheckersVariant, int[]>();
        foreach (var variant in new[] { CheckersVariant.International, CheckersVariant.English })
        {
            result[variant] = doc.RootElement.GetProperty(variant.ToString())
                .EnumerateArray()
                .Select(item => item.GetProperty("CalibratedDepth").GetInt32())
                .ToArray();
        }

        return result;
    }

    internal static string GetDocsBrainDirectory()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "brain");
        return Directory.Exists(dir)
            ? dir
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "brain"));
    }

    private static string? GetStringArg(string[] args, string name, string? fallback)
    {
        int idx = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : fallback;
    }

    private static int GetIntArg(string[] args, string name, int fallback) =>
        int.TryParse(GetStringArg(args, name, null), out int value) ? value : fallback;
}
