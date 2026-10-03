using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI.Benchmark;

/// <summary>
/// Executes the 40-position comparative benchmark between <see cref="BoardEngine.Array"/> and <see cref="BoardEngine.Bitboard"/>
/// across both <see cref="CheckersVariant.International"/> and <see cref="CheckersVariant.English"/> variants,
/// measuring both No-TT baseline and Default TT (1,048,576 entries) performance and verifying 100% search equivalence.
/// </summary>
public static class EngineBenchmarkRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<List<EngineBenchmarkResult>> RunVariantBenchmarkAsync(
        CheckersVariant variant,
        IReadOnlyList<BenchmarkPosition>? customPositions = null,
        Action<int, int, EngineBenchmarkResult>? progressCallback = null,
        bool deepCalibration = false,
        CancellationToken cancellationToken = default)
    {
        var positions = customPositions ?? BenchmarkSuite.GetPositions();
        var results = new List<EngineBenchmarkResult>(positions.Count);
        var arrayEngine = new RuleEngine(variant);
        var sharedTt = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);

        // Warmup JIT for both engines before timing
        WarmupEngines(positions[0].State, variant, sharedTt);

        for (int i = 0; i < positions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pos = positions[i];

            var result = await BenchmarkSinglePositionAsync(
                pos,
                variant,
                arrayEngine,
                sharedTt,
                deepCalibration,
                cancellationToken);

            results.Add(result);
            progressCallback?.Invoke(i + 1, positions.Count, result);
        }

        return results;
    }

    public static async Task<EngineBenchmarkResult> BenchmarkSinglePositionAsync(
        BenchmarkPosition pos,
        CheckersVariant variant,
        RuleEngine? arrayRuleEngine = null,
        TranspositionTable? sharedTt = null,
        bool deepCalibration = false,
        CancellationToken cancellationToken = default)
    {
        var ruleEngine = arrayRuleEngine ?? new RuleEngine(variant);
        var evaluator = new EvaluationFunction(variant);
        var legalMoves = ruleEngine.GetLegalMoves(pos.State);

        if (legalMoves.Count == 0)
            throw new InvalidOperationException($"Position {pos.Id} has no legal moves under {variant}.");

        int calibratedDepth;
        long arrayNoTtNodes;
        long arrayNoTtTimeMs;
        string arrayNoTtMove;
        string arrayNoTtScore;

        if (deepCalibration)
        {
            (calibratedDepth, arrayNoTtNodes, arrayNoTtTimeMs, arrayNoTtMove, arrayNoTtScore) =
                await CalibrateAndRunDeepArrayBaselineAsync(pos.State, legalMoves, ruleEngine, evaluator, cancellationToken);
        }
        else
        {
            calibratedDepth = CalibrateStandardDepth(pos.State, legalMoves, ruleEngine, evaluator);

            var arrNoTtPlayer = new MinimaxPlayer(
                limits: SearchLimits.FixedDepth(calibratedDepth),
                ruleEngine: ruleEngine,
                evaluator: evaluator,
                useTranspositionTable: false,
                useQuiescence: true);

            var sw = Stopwatch.StartNew();
            var move = await arrNoTtPlayer.GetMoveAsync(pos.State, legalMoves, cancellationToken);
            sw.Stop();

            arrayNoTtTimeMs = Math.Max(1, sw.ElapsedMilliseconds);
            arrayNoTtNodes = arrNoTtPlayer.NodesEvaluated;
            arrayNoTtMove = move.Notation;
            arrayNoTtScore = arrNoTtPlayer.LastAnalysis?.Value ?? "-";
        }

        // 2. Run Bitboard No-TT at identical calibratedDepth
        var bitNoTtPlayer = new BitboardMinimaxPlayer(
            limits: SearchLimits.FixedDepth(calibratedDepth),
            variant: variant,
            useTranspositionTable: false,
            useQuiescence: true);

        var bitNoTtSw = Stopwatch.StartNew();
        var bitNoTtMoveObj = await bitNoTtPlayer.GetMoveAsync(pos.State, legalMoves, cancellationToken);
        bitNoTtSw.Stop();

        long bitboardNoTtTimeMs = Math.Max(1, bitNoTtSw.ElapsedMilliseconds);
        long bitboardNoTtNodes = bitNoTtPlayer.NodesEvaluated;
        string bitboardNoTtMove = bitNoTtMoveObj.Notation;
        string bitboardNoTtScore = bitNoTtPlayer.LastAnalysis?.Value ?? "-";

        // 3. Run Array + Default TT (1,048,576 entries)
        var tt = sharedTt ?? TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
        tt.Clear();

        var arrTtPlayer = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(calibratedDepth),
            ruleEngine: ruleEngine,
            evaluator: evaluator,
            transpositionTable: tt,
            useTranspositionTable: true,
            useQuiescence: true);

        var arrTtSw = Stopwatch.StartNew();
        var arrTtMoveObj = await arrTtPlayer.GetMoveAsync(pos.State, legalMoves, cancellationToken);
        arrTtSw.Stop();

        long arrayTtTimeMs = Math.Max(1, arrTtSw.ElapsedMilliseconds);
        long arrayTtNodes = arrTtPlayer.NodesEvaluated;
        string arrayTtMove = arrTtMoveObj.Notation;
        string arrayTtScore = arrTtPlayer.LastAnalysis?.Value ?? "-";
        long arrayTtCutoffs = tt.Cutoffs;

        // 4. Run Bitboard + Default TT (1,048,576 entries)
        tt.Clear();

        var bitTtPlayer = new BitboardMinimaxPlayer(
            limits: SearchLimits.FixedDepth(calibratedDepth),
            variant: variant,
            transpositionTable: tt,
            useTranspositionTable: true,
            useQuiescence: true);

        var bitTtSw = Stopwatch.StartNew();
        var bitTtMoveObj = await bitTtPlayer.GetMoveAsync(pos.State, legalMoves, cancellationToken);
        bitTtSw.Stop();

        long bitboardTtTimeMs = Math.Max(1, bitTtSw.ElapsedMilliseconds);
        long bitboardTtNodes = bitTtPlayer.NodesEvaluated;
        string bitboardTtMove = bitTtMoveObj.Notation;
        string bitboardTtScore = bitTtPlayer.LastAnalysis?.Value ?? "-";
        long bitboardTtCutoffs = tt.Cutoffs;

        return new EngineBenchmarkResult
        {
            PositionId = pos.Id,
            Category = pos.Category,
            Description = pos.Description,
            Variant = variant,
            CalibratedDepth = calibratedDepth,

            ArrayNoTtNodes = arrayNoTtNodes,
            ArrayNoTtTimeMs = arrayNoTtTimeMs,
            ArrayNoTtMove = arrayNoTtMove,
            ArrayNoTtScore = arrayNoTtScore,

            BitboardNoTtNodes = bitboardNoTtNodes,
            BitboardNoTtTimeMs = bitboardNoTtTimeMs,
            BitboardNoTtMove = bitboardNoTtMove,
            BitboardNoTtScore = bitboardNoTtScore,

            ArrayTtNodes = arrayTtNodes,
            ArrayTtTimeMs = arrayTtTimeMs,
            ArrayTtMove = arrayTtMove,
            ArrayTtScore = arrayTtScore,
            ArrayTtCutoffs = arrayTtCutoffs,

            BitboardTtNodes = bitboardTtNodes,
            BitboardTtTimeMs = bitboardTtTimeMs,
            BitboardTtMove = bitboardTtMove,
            BitboardTtScore = bitboardTtScore,
            BitboardTtCutoffs = bitboardTtCutoffs
        };
    }

    private static void WarmupEngines(BoardState state, CheckersVariant variant, TranspositionTable tt)
    {
        var arrEngine = new RuleEngine(variant);
        var moves = arrEngine.GetLegalMoves(state);
        tt.Clear();
        var arrPlayer = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(4),
            ruleEngine: arrEngine,
            evaluator: new EvaluationFunction(variant),
            transpositionTable: tt,
            useTranspositionTable: true,
            useQuiescence: true);
        arrPlayer.GetMoveAsync(state, moves).AsTask().GetAwaiter().GetResult();

        tt.Clear();
        var bitPlayer = new BitboardMinimaxPlayer(
            limits: SearchLimits.FixedDepth(4),
            variant: variant,
            transpositionTable: tt,
            useTranspositionTable: true,
            useQuiescence: true);
        bitPlayer.GetMoveAsync(state, moves).AsTask().GetAwaiter().GetResult();
    }

    private static int CalibrateStandardDepth(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        RuleEngine ruleEngine,
        EvaluationFunction evaluator)
    {
        int depth = 5;

        while (depth < 9)
        {
            var testPlayer = new MinimaxPlayer(
                limits: SearchLimits.FixedDepth(depth),
                ruleEngine: ruleEngine,
                evaluator: evaluator,
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

    private static async Task<(int Depth, long Nodes, long TimeMs, string Move, string Score)> CalibrateAndRunDeepArrayBaselineAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        RuleEngine ruleEngine,
        EvaluationFunction evaluator,
        CancellationToken cancellationToken)
    {
        int bestDepth = 7;
        long bestNodes = 0;
        long bestTimeMs = 1;
        string bestMove = legalMoves[0].Notation;
        string bestScore = "-";
        long prevTimeMs = 0;

        for (int depth = 7; depth <= 15; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var testPlayer = new MinimaxPlayer(
                limits: SearchLimits.FixedDepth(depth),
                ruleEngine: ruleEngine,
                evaluator: evaluator,
                useTranspositionTable: false,
                useQuiescence: true);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(7500));

            var sw = Stopwatch.StartNew();
            try
            {
                var move = await testPlayer.GetMoveAsync(state, legalMoves, timeoutCts.Token);
                sw.Stop();

                long elapsedMs = Math.Max(1, sw.ElapsedMilliseconds);
                bestDepth = depth;
                bestNodes = testPlayer.NodesEvaluated;
                bestTimeMs = elapsedMs;
                bestMove = move.Notation;
                bestScore = testPlayer.LastAnalysis?.Value ?? "-";

                if (bestScore.Contains("Win", StringComparison.OrdinalIgnoreCase) ||
                    bestScore.Contains("Loss", StringComparison.OrdinalIgnoreCase) ||
                    elapsedMs >= 2200)
                {
                    break;
                }

                if (prevTimeMs >= 50 && elapsedMs >= 700)
                {
                    double branchingRatio = Math.Clamp((double)elapsedMs / prevTimeMs, 2.0, 10.0);
                    double predictedNextMs = elapsedMs * branchingRatio;
                    if (predictedNextMs > 8000)
                    {
                        break;
                    }
                }

                prevTimeMs = elapsedMs;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                break;
            }
        }

        return (bestDepth, bestNodes, bestTimeMs, bestMove, bestScore);
    }

    public static string FormatNoTtMarkdownTable(IReadOnlyList<EngineBenchmarkResult> results)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("| # | Category | Depth | Array Nodes (No-TT) | Bitboard Nodes (No-TT) | Array Time | Bitboard Time | Speedup | Identical |");
        sb.AppendLine("|---|:---:|:---:|---:|---:|---:|---:|---:|:---:|");

        long totalArrNodes = 0;
        long totalBitNodes = 0;
        long totalArrTime = 0;
        long totalBitTime = 0;
        bool allIdentical = true;

        foreach (var r in results)
        {
            totalArrNodes += r.ArrayNoTtNodes;
            totalBitNodes += r.BitboardNoTtNodes;
            totalArrTime += r.ArrayNoTtTimeMs;
            totalBitTime += r.BitboardNoTtTimeMs;
            if (!r.NoTtIdentical) allIdentical = false;

            string check = r.NoTtIdentical ? "Yes" : "MISMATCH";
            sb.AppendLine(string.Create(inv,
                $"| {r.PositionId} | {r.Category} | {r.CalibratedDepth} plies | {r.ArrayNoTtNodes:N0} | {r.BitboardNoTtNodes:N0} | {r.ArrayNoTtTimeMs:N0} ms | {r.BitboardNoTtTimeMs:N0} ms | **{r.NoTtSpeedupFactor:F2}x** | {check} |"));
        }

        double overallSpeedup = totalBitTime > 0 ? (double)totalArrTime / totalBitTime : 1.0;
        int minDepth = results.Count > 0 ? results.Min(r => r.CalibratedDepth) : 0;
        int maxDepth = results.Count > 0 ? results.Max(r => r.CalibratedDepth) : 0;

        sb.AppendLine("|---|:---:|:---:|---:|---:|---:|---:|---:|:---:|");
        sb.AppendLine(string.Create(inv,
            $"| **Total** | **All 40** | **{minDepth}â€“{maxDepth} plies** | **{totalArrNodes:N0}** | **{totalBitNodes:N0}** | **{totalArrTime:N0} ms** | **{totalBitTime:N0} ms** | **{overallSpeedup:F2}x** | **{(allIdentical ? "100%" : "FAIL")}** |"));

        return sb.ToString();
    }

    public static string FormatTtMarkdownTable(IReadOnlyList<EngineBenchmarkResult> results)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("| # | Category | Depth | Array TT Nodes (1M) | Bitboard TT Nodes (1M) | Array TT Time | Bitboard TT Time | Speedup | TT Cutoffs | Identical |");
        sb.AppendLine("|---|:---:|:---:|---:|---:|---:|---:|---:|---:|:---:|");

        long totalArrNodes = 0;
        long totalBitNodes = 0;
        long totalArrTime = 0;
        long totalBitTime = 0;
        long totalCutoffs = 0;
        bool allIdentical = true;

        foreach (var r in results)
        {
            totalArrNodes += r.ArrayTtNodes;
            totalBitNodes += r.BitboardTtNodes;
            totalArrTime += r.ArrayTtTimeMs;
            totalBitTime += r.BitboardTtTimeMs;
            totalCutoffs += r.BitboardTtCutoffs;
            if (!r.TtIdentical) allIdentical = false;

            string check = r.TtIdentical ? "Yes" : "MISMATCH";
            sb.AppendLine(string.Create(inv,
                $"| {r.PositionId} | {r.Category} | {r.CalibratedDepth} plies | {r.ArrayTtNodes:N0} | {r.BitboardTtNodes:N0} | {r.ArrayTtTimeMs:N0} ms | {r.BitboardTtTimeMs:N0} ms | **{r.TtSpeedupFactor:F2}x** | {r.BitboardTtCutoffs:N0} | {check} |"));
        }

        double overallSpeedup = totalBitTime > 0 ? (double)totalArrTime / totalBitTime : 1.0;
        int minDepth = results.Count > 0 ? results.Min(r => r.CalibratedDepth) : 0;
        int maxDepth = results.Count > 0 ? results.Max(r => r.CalibratedDepth) : 0;

        sb.AppendLine("|---|:---:|:---:|---:|---:|---:|---:|---:|---:|:---:|");
        sb.AppendLine(string.Create(inv,
            $"| **Total** | **All 40** | **{minDepth}â€“{maxDepth} plies** | **{totalArrNodes:N0}** | **{totalBitNodes:N0}** | **{totalArrTime:N0} ms** | **{totalBitTime:N0} ms** | **{overallSpeedup:F2}x** | **{totalCutoffs:N0}** | **{(allIdentical ? "100%" : "FAIL")}** |"));

        return sb.ToString();
    }

    public static string ToJson(object results) =>
        JsonSerializer.Serialize(results, JsonOptions);
}
