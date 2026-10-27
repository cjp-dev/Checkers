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
