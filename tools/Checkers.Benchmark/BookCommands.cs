using System.Diagnostics;
using System.Globalization;
using Checkers.Core.AI;
using Checkers.Core.AI.Book;
using Checkers.Core.Models;

namespace Checkers.Benchmark;

internal static class BookCommands
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        string subcommand = args[1].ToLowerInvariant();
        return subcommand switch
        {
            "generate" => Generate(args[2..]),
            "expand-doe" or "doe" => ExpandDoe(args[2..]),
            "verify" => Verify(args[2..]),
            _ => PrintUsage()
        };
    }

    private static int Generate(string[] args)
    {
        var variants = ParseVariants(GetArg(args, "--variant", "All"));
        int maxLevel = GetIntArg(args, "--max-level", -1);
        if (maxLevel < 0)
        {
            int depthArg = GetIntArg(args, "--depth", BookBuilder.DefaultMaxEvaluatedLevel + 1);
            maxLevel = Math.Max(0, depthArg - 1);
        }

        int width = GetIntArg(args, "--width", BookBuilder.DefaultWidth);
        double nodeTimeSec = GetDoubleArg(args, "--node-time-s", GetDoubleArg(args, "--leaf-time-s", 60.0));
        int nodeDepth = GetIntArg(args, "--node-depth", 0);
        int workers = GetIntArg(args, "--workers", BookBuilder.DefaultWorkers);
        string? customOut = GetArg(args, "--out", null);

        SearchLimits searchLimits = nodeDepth > 0
            ? SearchLimits.FixedDepth(nodeDepth, useOpeningBook: false)
            : SearchLimits.TimePerMove(TimeSpan.FromSeconds(Math.Max(0.05, nodeTimeSec)), useOpeningBook: false);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[Ctrl+C] Graceful stop requested — saving current progress to .partial checkpoint...");
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            foreach (var variant in variants)
            {
                string outputPath = !string.IsNullOrWhiteSpace(customOut) && variants.Count == 1
                    ? Path.GetFullPath(customOut)
                    : GetDefaultBookPath(variant);

                int rc = GenerateForVariant(variant, maxLevel, width, searchLimits, nodeTimeSec, nodeDepth, workers, outputPath, cts.Token);
                if (rc != 0)
                {
                    return rc;
                }
            }

            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static int GenerateForVariant(
        CheckersVariant variant,
        int maxLevel,
        int width,
        SearchLimits searchLimits,
        double nodeTimeSec,
        int nodeDepth,
        int workers,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string partialPath = outputPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var checkpointNodes = new Dictionary<ulong, BookNode>();
        if (File.Exists(partialPath))
        {
            using (var reader = new StreamReader(partialPath))
            {
                checkpointNodes = BookFile.ReadPartialNodes(reader);
            }

            // Rewrite cleanly without any truncated trailing line
            using (var cleanWriter = new StreamWriter(partialPath, append: false) { NewLine = "\n" })
            {
                cleanWriter.WriteLine($"# Checkers Opening Book Partial Checkpoint ({variant}) — maxLevel {maxLevel}, width {width}");
                foreach (var node in checkpointNodes.Values.OrderBy(n => n.Ply).ThenBy(n => n.ZobristHash))
                {
                    cleanWriter.WriteLine(BookFile.Format(node));
                }
            }
        }

        string limitDescription = nodeDepth > 0
            ? $"fixed depth {nodeDepth} plies/node"
            : $"{nodeTimeSec:0.#}s Multi-PV ({width}) search/node";

        Console.WriteLine("====================================================================================");
        Console.WriteLine($"  Opening Book Generation — {variant} Checkers (Top-Down Level-by-Level + BackUp)");
        Console.WriteLine($"  Levels: lv 0 .. lv {maxLevel} ({maxLevel + 1} plies of moves, reaching lv {maxLevel + 1}) | Width: {width}");
        Console.WriteLine($"  Search: {limitDescription} | Workers: {workers} (32 MiB TT/worker)");
        Console.WriteLine($"  Output: {outputPath}");
        if (checkpointNodes.Count > 0)
        {
            Console.WriteLine($"  Resume: Loaded {checkpointNodes.Count:N0} previously evaluated positions from {Path.GetFileName(partialPath)}");
        }
        Console.WriteLine("  Press Ctrl+C at any time to stop safely; run again to resume from .partial.");
        Console.WriteLine("====================================================================================");

        var overallClock = Stopwatch.StartNew();
        var levelClock = Stopwatch.StartNew();
        int currentTrackedLevel = -1;
        int newlySearchedInLevel = 0;
        int cachedInLevel = 0;

        List<BookNode> finalBook;
        try
        {
            using var partialWriter = new StreamWriter(partialPath, append: true) { AutoFlush = true, NewLine = "\n" };

            finalBook = BookBuilder.GenerateTopDown(
                variant,
                maxEvaluatedLevel: maxLevel,
                width: width,
                nodeSearchLimits: searchLimits,
                workers: workers,
                checkpointNodes: checkpointNodes,
                onNodeEvaluated: progress =>
                {
                    if (progress.Level != currentTrackedLevel)
                    {
                        currentTrackedLevel = progress.Level;
                        newlySearchedInLevel = 0;
                        cachedInLevel = 0;
                        levelClock.Restart();
                    }

                    if (progress.FromCheckpoint)
                    {
                        cachedInLevel++;
                        if (progress.CompletedInLevel == progress.TotalInLevel)
                        {
                            Console.WriteLine(
                                $"  [lv {progress.Level}] Restored {cachedInLevel}/{progress.TotalInLevel} positions from checkpoint.");
                        }
                        return;
                    }

                    newlySearchedInLevel++;
                    partialWriter.WriteLine(BookFile.Format(progress.EvaluatedNode));

                    int remainingInLevel = Math.Max(0, progress.TotalInLevel - progress.CompletedInLevel);
                    double elapsedSec = Math.Max(0.001, levelClock.Elapsed.TotalSeconds);
                    double ratePerSec = newlySearchedInLevel / elapsedSec;
                    string etaText = ratePerSec > 0
                        ? FormatDuration(TimeSpan.FromSeconds(remainingInLevel / ratePerSec))
                        : "?";

                    string movesPreview = string.Join(
                        ' ',
                        progress.EvaluatedNode.Moves.Select(m => $"{m.Notation}:{BookFile.FormatSignedScore(m.Score)}"));

                    Console.WriteLine(
                        $"  [lv {progress.Level} | {progress.CompletedInLevel,4}/{progress.TotalInLevel,-4} ({100.0 * progress.CompletedInLevel / Math.Max(1, progress.TotalInLevel),5:F1}%)] " +
                        $"{progress.EvaluatedNode.ZobristHash:X16} d={progress.CompletedDepth,2} ({progress.NodesEvaluated / 1_000_000.0,5:F1}M nodes) " +
                        $"[{movesPreview}] | lvl ETA {etaText}");
                },
                onLevelComplete: (completedLevel, backedUpSnapshot) =>
                {
                    WriteBookToFile(outputPath, variant, completedLevel + 1, width, limitDescription, workers, overallClock.Elapsed, backedUpSnapshot);
                    int internalCount = backedUpSnapshot.Count(n => n.Moves.Count > 0);
                    int leafCount = backedUpSnapshot.Count - internalCount;
                    Console.WriteLine(
                        $"  --> Completed lv {completedLevel}: flushed playable {completedLevel + 1}-ply book to {Path.GetFileName(outputPath)} " +
                        $"({backedUpSnapshot.Count:N0} total nodes: {internalCount:N0} internal, {leafCount:N0} frontier leaves, elapsed {FormatDuration(overallClock.Elapsed)}).\n");
                    levelClock.Restart();
                },
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(
                $"\nStopped safely: {checkpointNodes.Count:N0} evaluated positions saved in {partialPath}. " +
                "Run the same command again to resume.");
            return 2;
        }

        var inconsistent = BookBuilder.CheckBackUp(finalBook, variant);
        if (inconsistent.Count > 0)
        {
            Console.WriteLine($"WARNING: {inconsistent.Count} inconsistent nodes detected during CheckBackUp.");
            return 1;
        }

        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        Console.WriteLine($"Successfully generated and verified {variant} opening book ({finalBook.Count:N0} positions) in {FormatDuration(overallClock.Elapsed)}.");
        PrintBookSummary(finalBook, variant);
        return 0;
    }

    private static int ExpandDoe(string[] args)
    {
        var variants = ParseVariants(GetArg(args, "--variant", "All"));
        int fullWidthPlies = GetIntArg(args, "--full-width-plies", BookBuilder.DefaultFullWidthPlies);
        int maxPly = GetIntArg(args, "--max-ply", BookBuilder.DefaultMaxPly);
        int width = GetIntArg(args, "--width", BookBuilder.DefaultWidth);
        int? deltaCp = GetNullableIntArg(args, "--delta-cp");
        int? maxIterations = GetNullableIntArg(args, "--iterations");
        double? maxTimeMinutes = GetNullableDoubleArg(args, "--max-time-min");
        double nodeTimeSec = GetDoubleArg(args, "--node-time-s", GetDoubleArg(args, "--leaf-time-s", 60.0));
        int nodeDepth = GetIntArg(args, "--node-depth", 0);
        int workers = GetIntArg(args, "--workers", BookBuilder.DefaultWorkers);
        int flushInterval = GetIntArg(args, "--flush-interval", 25);
        string? customOut = GetArg(args, "--out", null);

        SearchLimits searchLimits = nodeDepth > 0
            ? SearchLimits.FixedDepth(nodeDepth, useOpeningBook: false)
            : SearchLimits.TimePerMove(TimeSpan.FromSeconds(Math.Max(0.05, nodeTimeSec)), useOpeningBook: false);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[Ctrl+C] Graceful stop requested — saving current progress to .partial checkpoint...");
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            foreach (var variant in variants)
            {
                string outputPath = !string.IsNullOrWhiteSpace(customOut) && variants.Count == 1
                    ? Path.GetFullPath(customOut)
                    : GetDefaultBookPath(variant);

                int rc = ExpandDoeForVariant(
                    variant,
                    fullWidthPlies,
                    maxPly,
                    width,
                    deltaCp,
                    maxIterations,
                    maxTimeMinutes,
                    searchLimits,
                    nodeTimeSec,
                    nodeDepth,
                    workers,
                    flushInterval,
                    outputPath,
                    cts.Token);

                if (rc != 0)
                {
                    return rc;
                }
            }

            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static int ExpandDoeForVariant(
        CheckersVariant variant,
        int fullWidthPlies,
        int maxPly,
        int width,
        int? deltaCp,
        int? maxIterations,
        double? maxTimeMinutes,
        SearchLimits searchLimits,
        double nodeTimeSec,
        int nodeDepth,
        int workers,
        int flushInterval,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string partialPath = outputPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        List<BookNode> existingNodes = [];
        if (File.Exists(outputPath))
        {
            using var reader = new StreamReader(outputPath);
            existingNodes = BookFile.Read(reader);
        }

        var checkpointNodes = new Dictionary<ulong, BookNode>();
        if (File.Exists(partialPath))
        {
            using (var reader = new StreamReader(partialPath))
            {
                checkpointNodes = BookFile.ReadPartialNodes(reader);
            }

            using (var cleanWriter = new StreamWriter(partialPath, append: false) { NewLine = "\n" })
            {
                cleanWriter.WriteLine($"# Checkers Opening Book DOE Partial Checkpoint ({variant}) — fullWidthPlies {fullWidthPlies}, maxPly {maxPly}, width {width}");
                foreach (var node in checkpointNodes.Values.OrderBy(n => n.Ply).ThenBy(n => n.ZobristHash))
                {
                    cleanWriter.WriteLine(BookFile.Format(node));
                }
            }
        }

        string limitDescription = nodeDepth > 0
            ? $"fixed depth {nodeDepth} plies/node"
            : $"{nodeTimeSec:0.#}s Multi-PV search/node";

        string deltaDescription = deltaCp.HasValue
            ? $"flat {deltaCp.Value} cp"
            : "tapered (lv 0..5: 15 cp, lv 6..9: 10 cp, lv 10+: 6 cp)";

        string budgetDescription = (maxIterations, maxTimeMinutes) switch
        {
            (int it, double tm) => $"max {it:N0} expansions or {tm:0.#} min",
            (int it, null) => $"max {it:N0} expansions",
            (null, double tm) => $"max {tm:0.#} min",
            _ => $"until all active lines reach lv {maxPly}"
        };

        Console.WriteLine("====================================================================================");
        Console.WriteLine($"  Opening Book Drop-Out Expansion (DOE) — {variant} Checkers");
        Console.WriteLine($"  Phase A: Full-Width Early Plies lv 0..{Math.Max(0, fullWidthPlies - 1)} ({fullWidthPlies} plies storing ALL legal moves)");
        Console.WriteLine($"  Phase B: DOE up to lv {maxPly} | Width: {width} | Delta: {deltaDescription}");
        Console.WriteLine($"  Budget : {budgetDescription}");
        Console.WriteLine($"  Search : {limitDescription} | Workers: {workers} (32 MiB TT/worker)");
        Console.WriteLine($"  Book   : {outputPath} ({existingNodes.Count:N0} existing nodes loaded)");
        if (checkpointNodes.Count > 0)
        {
            Console.WriteLine($"  Resume : Loaded {checkpointNodes.Count:N0} checkpoint positions from {Path.GetFileName(partialPath)}");
        }
        Console.WriteLine("  Press Ctrl+C at any time to stop safely; run again to resume from .partial.");
        Console.WriteLine("====================================================================================");

        var overallClock = Stopwatch.StartNew();
        var phaseClock = Stopwatch.StartNew();
        string currentTrackedPhase = "";
        int cachedInPhase = 0;
        int newlySearchedInPhase = 0;

        List<BookNode> finalBook;
        try
        {
            using var partialWriter = new StreamWriter(partialPath, append: true) { AutoFlush = true, NewLine = "\n" };

            finalBook = BookBuilder.ExpandDropOut(
                variant,
                existingNodes: existingNodes,
                fullWidthPlies: fullWidthPlies,
                maxPly: maxPly,
                width: width,
                fixedDeltaCp: deltaCp,
                maxIterations: maxIterations,
                maxTimeMinutes: maxTimeMinutes,
                nodeSearchLimits: searchLimits,
                workers: workers,
                checkpointNodes: checkpointNodes,
                onNodeExpanded: progress =>
                {
                    if (!string.Equals(progress.Phase, currentTrackedPhase, StringComparison.Ordinal))
                    {
                        currentTrackedPhase = progress.Phase;
                        cachedInPhase = 0;
                        newlySearchedInPhase = 0;
                        phaseClock.Restart();
                    }

                    if (progress.FromCheckpoint)
                    {
                        cachedInPhase++;
                        if (progress.TotalInPhase.HasValue && progress.CompletedInPhase == progress.TotalInPhase.Value)
                        {
                            Console.WriteLine(
                                $"  [{progress.Phase}] Restored {cachedInPhase}/{progress.TotalInPhase.Value} already-complete positions.");
                        }
                        return;
                    }

                    newlySearchedInPhase++;
                    partialWriter.WriteLine(BookFile.Format(progress.EvaluatedNode));

                    string countText = progress.TotalInPhase.HasValue
                        ? $"{progress.CompletedInPhase,4}/{progress.TotalInPhase.Value,-4}"
                        : $"#{progress.CompletedInPhase,-4}";

                    string movesPreview = string.Join(
                        ' ',
                        progress.EvaluatedNode.Moves.Take(5).Select(m => $"{m.Notation}:{BookFile.FormatSignedScore(m.Score)}"));
                    if (progress.EvaluatedNode.Moves.Count > 5)
                    {
                        movesPreview += $" (+{progress.EvaluatedNode.Moves.Count - 5} more)";
                    }

                    Console.WriteLine(
                        $"  [{progress.Phase} | {countText} | lv {progress.EvaluatedNode.Ply,2}] " +
                        $"{progress.EvaluatedNode.ZobristHash:X16} d={progress.CompletedDepth,2} ({progress.NodesEvaluated / 1_000_000.0,5:F1}M nodes) " +
                        $"[{movesPreview}] | book {progress.TotalBookNodes:N0} ({progress.ActiveFrontierLeaves:N0} active leaves)");
                },
                onFlushSnapshot: (label, snapshot) =>
                {
                    int currentMaxPly = snapshot.Count > 0 ? snapshot.Max(n => n.Ply) : 0;
                    WriteBookToFile(outputPath, variant, currentMaxPly, width, $"DOE ({deltaDescription}, {limitDescription})", workers, overallClock.Elapsed, snapshot);
                    int internalCount = snapshot.Count(n => n.Moves.Count > 0);
                    int leafCount = snapshot.Count - internalCount;
                    Console.WriteLine(
                        $"  --> [{label}] Flushed {variant} book to {Path.GetFileName(outputPath)} " +
                        $"({snapshot.Count:N0} nodes: {internalCount:N0} internal, {leafCount:N0} leaves, max ply {currentMaxPly}, elapsed {FormatDuration(overallClock.Elapsed)}).\n");
                },
                snapshotFlushInterval: flushInterval,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(
                $"\nStopped safely: {checkpointNodes.Count:N0} evaluated positions saved in {partialPath}. " +
                "Run the same command again to resume.");
            return 2;
        }

        var inconsistent = BookBuilder.CheckBackUp(finalBook, variant);
        if (inconsistent.Count > 0)
        {
            Console.WriteLine($"WARNING: {inconsistent.Count} inconsistent nodes detected during CheckBackUp.");
            return 1;
        }

        int finalMaxPly = finalBook.Count > 0 ? finalBook.Max(n => n.Ply) : 0;
        WriteBookToFile(outputPath, variant, finalMaxPly, width, $"DOE ({deltaDescription}, {limitDescription})", workers, overallClock.Elapsed, finalBook);

        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        Console.WriteLine($"Successfully expanded and verified {variant} opening book ({finalBook.Count:N0} positions) in {FormatDuration(overallClock.Elapsed)}.");
        PrintBookSummary(finalBook, variant);
        return 0;
    }

    private static int Verify(string[] args)
    {
        string? customBookPath = GetArg(args, "--book", null);
        var variants = ParseVariants(GetArg(args, "--variant", "All"));
        int failures = 0;

        if (!string.IsNullOrWhiteSpace(customBookPath))
        {
            CheckersVariant variant = customBookPath.Contains("English", StringComparison.OrdinalIgnoreCase)
                ? CheckersVariant.English
                : CheckersVariant.International;
            failures += VerifySingleBook(Path.GetFullPath(customBookPath), variant);
        }
        else
        {
            foreach (var variant in variants)
            {
                failures += VerifySingleBook(GetDefaultBookPath(variant), variant);
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static int VerifySingleBook(string path, CheckersVariant variant)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[Verify] {variant}: File not found at {path}");
            return 1;
        }

        List<BookNode> nodes;
        using (var reader = new StreamReader(path))
        {
            nodes = BookFile.Read(reader);
        }

        Console.WriteLine("------------------------------------------------------------------------------------");
        Console.WriteLine($"[Verify] {variant} Opening Book: {path}");
        if (nodes.Count == 0)
        {
            Console.WriteLine("  Book is empty (0 positions).");
            return 0;
        }

        var inconsistent = BookBuilder.CheckBackUp(nodes, variant);
        PrintBookSummary(nodes, variant);
        Console.WriteLine($"  Negamax Back-Up Consistency Check: {(inconsistent.Count == 0 ? "OK (0 errors)" : $"FAILED ({inconsistent.Count} inconsistent nodes)")}");

        foreach (var bad in inconsistent.Take(10))
        {
            Console.WriteLine($"    Inconsistent node: {BookFile.Format(bad)}");
        }

        return inconsistent.Count;
    }

    private static void PrintBookSummary(IReadOnlyList<BookNode> nodes, CheckersVariant variant)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        int maxPly = nodes.Max(n => n.Ply);
        int internalNodes = nodes.Count(n => n.Moves.Count > 0);
        int leafNodes = nodes.Count - internalNodes;
        Console.WriteLine($"  Total Unique Positions: {nodes.Count:N0} ({internalNodes:N0} evaluated internal positions, {leafNodes:N0} frontier leaves, max ply {maxPly})");

        var byPly = nodes
            .GroupBy(n => n.Ply)
            .OrderBy(g => g.Key)
            .Select(g => $"lv {g.Key}: {g.Count():N0}")
            .ToList();
        Console.WriteLine($"  By Level: {string.Join(" | ", byPly)}");

        var root = nodes.FirstOrDefault(n => n.Ply == 0);
        if (root != null)
        {
            string rootMoves = string.Join(
                ", ",
                root.Moves.Select(m => $"{m.Notation} ({BookFile.FormatSignedScore(m.Score)})"));
            Console.WriteLine($"  Root (lv 0, {variant}): Score {BookFile.FormatSignedScore(root.Score)} | Best moves: {rootMoves}");
        }
    }

    private static void WriteBookToFile(
        string outputPath,
        CheckersVariant variant,
        int depth,
        int width,
        string limitDescription,
        int workers,
        TimeSpan elapsed,
        IReadOnlyList<BookNode> nodes)
    {
        int internalCount = nodes.Count(n => n.Moves.Count > 0);
        int leafCount = nodes.Count - internalCount;
        string comment =
            $"Checkers Opening Book ({variant}) — depth {depth} plies (lv 0..{depth - 1} evaluated), width {width}, {nodes.Count} unique positions ({internalCount} internal, {leafCount} leaves)\n" +
            "Format: <ZobristHex> <Ply> <Score> [Move1:Score1 Move2:Score2 Move3:Score3]\n" +
            $"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Checkers.Benchmark book generate ({limitDescription}, {workers} workers, {FormatDuration(elapsed)})";

        string tempPath = outputPath + ".tmp";
        using (var writer = new StreamWriter(tempPath, append: false) { NewLine = "\n" })
        {
            BookFile.Write(writer, nodes, comment);
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static string GetDefaultBookPath(CheckersVariant variant)
    {
        string fileName = variant == CheckersVariant.English
            ? "OpeningBook.English.txt"
            : "OpeningBook.International.txt";

        string candidate = Path.Combine(Directory.GetCurrentDirectory(), "src", "Checkers.Core", "AI", "Book", fileName);
        if (Directory.Exists(Path.GetDirectoryName(candidate)))
        {
            return Path.GetFullPath(candidate);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Checkers.Core", "AI", "Book", fileName));
    }

    private static List<CheckersVariant> ParseVariants(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return [CheckersVariant.English, CheckersVariant.International];
        }

        if (text.Equals("English", StringComparison.OrdinalIgnoreCase))
        {
            return [CheckersVariant.English];
        }

        if (text.Equals("International", StringComparison.OrdinalIgnoreCase))
        {
            return [CheckersVariant.International];
        }

        throw new ArgumentException($"Unknown --variant '{text}'. Expected English, International, or All.");
    }

    private static string? GetArg(string[] args, string name, string? defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return defaultValue;
    }

    private static int GetIntArg(string[] args, string name, int defaultValue)
    {
        string? raw = GetArg(args, name, null);
        return raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val)
            ? val
            : defaultValue;
    }

    private static int? GetNullableIntArg(string[] args, string name)
    {
        string? raw = GetArg(args, name, null);
        return raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val)
            ? val
            : null;
    }

    private static double GetDoubleArg(string[] args, string name, double defaultValue)
    {
        string? raw = GetArg(args, name, null);
        return raw != null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double val)
            ? val
            : defaultValue;
    }

    private static double? GetNullableDoubleArg(string[] args, string name)
    {
        string? raw = GetArg(args, name, null);
        return raw != null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double val)
            ? val
            : null;
    }

    private static string FormatDuration(TimeSpan time) =>
        time.TotalHours >= 1
            ? $"{(int)time.TotalHours}h {time.Minutes:D2}m {time.Seconds:D2}s"
            : $"{time.Minutes:D2}m {time.Seconds:D2}s";

    private static int PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -c Release --project tools/Checkers.Benchmark -- book expand-doe [options]");
        Console.WriteLine("  dotnet run -c Release --project tools/Checkers.Benchmark -- book generate [options]");
        Console.WriteLine("  dotnet run -c Release --project tools/Checkers.Benchmark -- book verify [options]");
        Console.WriteLine();
        Console.WriteLine("Options for 'book expand-doe' (Full-Width Early Plies + Drop-Out Expansion):");
        Console.WriteLine("  --variant <English|International|All>  Variant(s) to expand (default: All)");
        Console.WriteLine("  --full-width-plies <count>             Early levels storing ALL legal moves (default: 4, i.e. lv 0..3)");
        Console.WriteLine("  --max-ply <plies>                      Maximum depth ceiling per active line (default: 12)");
        Console.WriteLine("  --width <1..32>                        Top moves stored per DOE leaf at lv >= full-width-plies (default: 3)");
        Console.WriteLine("  --delta-cp <cp>                        Optional flat drop-out margin (default: tapered 15/10/6 cp)");
        Console.WriteLine("  --iterations <count>                   Optional max number of newly searched positions");
        Console.WriteLine("  --max-time-min <minutes>               Optional wall-clock time budget in minutes per variant");
        Console.WriteLine("  --node-time-s <seconds>                Search time per position in seconds (default: 60)");
        Console.WriteLine("  --node-depth <plies>                   Optional fixed search depth per node (overrides --node-time-s)");
        Console.WriteLine("  --workers <count>                      Parallel worker threads (default: 8 for Ryzen 7 7800X3D)");
        Console.WriteLine("  --flush-interval <count>               Newly expanded nodes between book flushes (default: 25)");
        Console.WriteLine("  --out <path>                           Optional custom book file path");
        Console.WriteLine();
        Console.WriteLine("Options for 'book generate' (Uniform Top-Down Level-by-Level):");
        Console.WriteLine("  --variant <English|International|All>  Variant(s) to generate (default: All)");
        Console.WriteLine("  --max-level <0..31>                    Deepest level to evaluate (default: 7, i.e. lv 0..7 = 8 plies of moves)");
        Console.WriteLine("  --width <1..32>                        Top moves to store per position (default: 3)");
        Console.WriteLine("  --node-time-s <seconds>                Search time per position in seconds (default: 60)");
        Console.WriteLine("  --node-depth <plies>                   Optional fixed search depth per node (overrides --node-time-s)");
        Console.WriteLine("  --workers <count>                      Parallel worker threads (default: 8 for Ryzen 7 7800X3D)");
        Console.WriteLine("  --out <path>                           Optional custom output file path");
        return 1;
    }
}

