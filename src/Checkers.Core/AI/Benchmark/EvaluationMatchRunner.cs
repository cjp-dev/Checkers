using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Checkers.Core.Bitboards;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI.Benchmark;

public sealed record OpeningBallot(
    int BallotId,
    int PliesFromStart,
    int PieceCount,
    int LegacyEvalCp,
    int NewEvalCp,
    BoardState State,
    IReadOnlyList<ulong> InitialHashHistory);

public sealed record MatchGameResult(
    int GameId,
    int BallotId,
    bool NewEvalIsWhite,
    int NewEvalPointsHalf, // 2 = NewEval Win, 1 = Draw, 0 = LegacyEval Win
    string TerminationReason,
    int TotalPlies,
    double AvgNewDepth,
    double AvgLegacyDepth,
    double AvgNewNpsMillions,
    double AvgLegacyNpsMillions);

public sealed record VariantMatchSummary(
    CheckersVariant Variant,
    int TimePerMoveMs,
    int Workers,
    int TotalGames,
    int NewEvalWins,
    int Draws,
    int LegacyEvalWins,
    int NewWhiteWins,
    int NewWhiteDraws,
    int NewWhiteLosses,
    int NewBlackWins,
    int NewBlackDraws,
    int NewBlackLosses,
    int BallotSweepsWin,    // 2.0 - 0.0
    int BallotMiniWins,     // 1.5 - 0.5
    int BallotSplits,       // 1.0 - 1.0
    int BallotMiniLosses,   // 0.5 - 1.5
    int BallotSweepsLoss,   // 0.0 - 2.0
    double ScorePercentage,
    double EloDifference,
    double EloError95,
    double LikelihoodOfSuperiority,
    double AvgPliesPerGame,
    double AvgNewEvalDepth,
    double AvgLegacyEvalDepth,
    double AvgNewEvalNpsMillions,
    double AvgLegacyEvalNpsMillions,
    double TotalWallTimeSeconds,
    IReadOnlyList<MatchGameResult> Games);

public sealed record MicrobenchVariantResult(
    CheckersVariant Variant,
    long TotalEvaluations,
    long LegacyElapsedMs,
    double LegacyMillionEvalsPerSec,
    double LegacyNsPerEval,
    long NewElapsedMs,
    double NewMillionEvalsPerSec,
    double NewNsPerEval,
    long Checksum);

public sealed record SearchSpeedVariantResult(
    CheckersVariant Variant,
    long LegacyNoTtNodes,
    long LegacyNoTtMs,
    double LegacyNoTtNpsM,
    long NewNoTtNodes,
    long NewNoTtMs,
    double NewNoTtNpsM,
    long LegacyTt1MNodes,
    long LegacyTt1MMs,
    double LegacyTt1MNpsM,
    long LegacyTt1MCutoffs,
    long NewTt1MNodes,
    long NewTt1MMs,
    double NewTt1MNpsM,
    long NewTt1MCutoffs);

public sealed record TimedPositionComparisonResult(
    CheckersVariant Variant,
    int PositionId,
    string Category,
    string LegacyDepth,
    string LegacyMove,
    string LegacyScore,
    long LegacyNodes,
    long LegacyTimeMs,
    double LegacyNpsM,
    string NewDepth,
    string NewMove,
    string NewScore,
    long NewNodes,
    long NewTimeMs,
    double NewNpsM);

public sealed record EvaluationSpeedBenchmarkReport(
    IReadOnlyList<MicrobenchVariantResult> Microbenchmarks,
    IReadOnlyList<SearchSpeedVariantResult> FixedDepthBenchmarks,
    IReadOnlyList<TimedPositionComparisonResult> TimedBenchmarks);

/// <summary>
/// Executes the Evaluation Speed Benchmark (<c>eval-speed</c>) and the 50-Position (100-Game)
/// Bot-vs-Bot Self-Play Match Harness (<c>eval-match</c>) comparing <see cref="EvaluationFunction"/>
/// against <see cref="LegacyEvaluationFunction"/> with complete per-engine data structure isolation.
/// </summary>
public static class EvaluationMatchRunner
{
    /// <summary>
    /// Generates <paramref name="count"/> deterministic, unique, balanced opening ballot positions
    /// between 6 and 10 plies from the initial board for the specified <paramref name="variant"/>.
    /// </summary>
    public static IReadOnlyList<OpeningBallot> GenerateBalancedBallots(CheckersVariant variant, int count = 50)
    {
        var ruleEngine = new RuleEngine(variant);
        var legacyEval = new LegacyEvaluationFunction(variant);
        var newEval = new EvaluationFunction(variant);
        var seenHashes = new HashSet<ulong>();
        var ballots = new List<OpeningBallot>(count);

        int seed = variant == CheckersVariant.English ? 10_000 : 50_000;

        while (ballots.Count < count)
        {
            int targetPlies = 6 + (ballots.Count % 5); // 10 ballots each at 6, 7, 8, 9, and 10 plies
            var rng = new Random(seed++);
            var state = BoardState.CreateInitial();
            var history = new List<ulong>(targetPlies + 1) { state.ZobristHash };
            bool validSequence = true;

            for (int ply = 0; ply < targetPlies; ply++)
            {
                var legalMoves = ruleEngine.GetLegalMoves(state);
                if (legalMoves.Count == 0)
                {
                    validSequence = false;
                    break;
                }

                var move = legalMoves[rng.Next(legalMoves.Count)];
                state = ruleEngine.ApplyMove(state, move);
                history.Add(state.ZobristHash);
            }

            if (!validSequence)
                continue;

            // Must have equal piece counts (12v12 or 11v11 men, 0 kings)
            if (state.WhitePiecesCount != state.BlackPiecesCount ||
                state.WhitePiecesCount < 10 ||
                state.WhiteKingsCount != 0 ||
                state.BlackKingsCount != 0)
            {
                continue;
            }

            var currentLegal = ruleEngine.GetLegalMoves(state);
            // Must be a quiet position with at least 3 legal moves
            if (currentLegal.Count < 3 || currentLegal[0].IsCapture)
                continue;

            if (!seenHashes.Add(state.ZobristHash))
                continue;

            // Shallow search verification (depth 6) with both evaluators to ensure position is even (|eval| <= 30 cp)
            var legacyCheck = new MinimaxPlayer(
                SearchLimits.FixedDepth(6),
                ruleEngine: ruleEngine,
                evaluator: legacyEval,
                useTranspositionTable: false,
                useQuiescence: true,
                variant: variant);

            var newCheck = new MinimaxPlayer(
                SearchLimits.FixedDepth(6),
                ruleEngine: ruleEngine,
                evaluator: newEval,
                useTranspositionTable: false,
                useQuiescence: true,
                variant: variant);

            legacyCheck.GetMoveAsync(state, currentLegal).GetAwaiter().GetResult();
            newCheck.GetMoveAsync(state, currentLegal).GetAwaiter().GetResult();

            int legacyScore = ParseScore(legacyCheck.LastAnalysis?.Value);
            int newScore = ParseScore(newCheck.LastAnalysis?.Value);

            if (Math.Abs(legacyScore) > 30 || Math.Abs(newScore) > 30)
                continue;

            ballots.Add(new OpeningBallot(
                BallotId: ballots.Count + 1,
                PliesFromStart: targetPlies,
                PieceCount: state.WhitePiecesCount + state.BlackPiecesCount,
                LegacyEvalCp: legacyScore,
                NewEvalCp: newScore,
                State: state.Clone(),
                InitialHashHistory: history));
        }

        return ballots;
    }

    /// <summary>
    /// Runs the 3-part Evaluation Speed Benchmark comparing <see cref="LegacyEvaluationFunction"/>
    /// against <see cref="EvaluationFunction"/> across both English and International variants.
    /// </summary>
    public static async Task<EvaluationSpeedBenchmarkReport> RunSpeedBenchmarkAsync(
        string? baselineJsonPath = null,
        double timedSecondsPerPosition = 5.0,
        Action<string>? log = null)
    {
        var positions = BenchmarkSuite.GetPositions();
        var bitPositions = positions.Select(p => p.State.BitPosition).ToArray();

        log?.Invoke("====================================================================================");
        log?.Invoke("  PART A: Raw Static Evaluation Microbenchmark (10,000,000 Evaluations / Variant)");
        log?.Invoke("====================================================================================");

        var microResults = new List<MicrobenchVariantResult>(2);
        const int passes = 250_000; // 250,000 * 40 positions = 10,000,000 evaluations
        const long totalEvals = (long)passes * 40;

        foreach (var variant in new[] { CheckersVariant.English, CheckersVariant.International })
        {
            // Warmup JIT
            long warmSum = RunRawLegacyLoop(bitPositions, variant, 10_000)
                         + RunRawNewLoop(bitPositions, variant, 10_000);

            var swLegacy = Stopwatch.StartNew();
            long sumLegacy = RunRawLegacyLoop(bitPositions, variant, passes);
            swLegacy.Stop();

            var swNew = Stopwatch.StartNew();
            long sumNew = RunRawNewLoop(bitPositions, variant, passes);
            swNew.Stop();

            double legacySec = Math.Max(0.0001, swLegacy.Elapsed.TotalSeconds);
            double newSec = Math.Max(0.0001, swNew.Elapsed.TotalSeconds);

            var res = new MicrobenchVariantResult(
                Variant: variant,
                TotalEvaluations: totalEvals,
                LegacyElapsedMs: swLegacy.ElapsedMilliseconds,
                LegacyMillionEvalsPerSec: (totalEvals / 1_000_000.0) / legacySec,
                LegacyNsPerEval: (legacySec * 1_000_000_000.0) / totalEvals,
                NewElapsedMs: swNew.ElapsedMilliseconds,
                NewMillionEvalsPerSec: (totalEvals / 1_000_000.0) / newSec,
                NewNsPerEval: (newSec * 1_000_000_000.0) / totalEvals,
                Checksum: warmSum + sumLegacy + sumNew);

            microResults.Add(res);
            log?.Invoke(
                $"  [{variant,-13}] Legacy: {res.LegacyElapsedMs,4} ms ({res.LegacyMillionEvalsPerSec,6:F1}M evals/s, {res.LegacyNsPerEval,4:F1} ns/eval) | " +
                $"New: {res.NewElapsedMs,4} ms ({res.NewMillionEvalsPerSec,6:F1}M evals/s, {res.NewNsPerEval,4:F1} ns/eval)");
        }

        log?.Invoke("\n====================================================================================");
        log?.Invoke("  PART B: 40-Position Fixed-Depth Search Speed Comparison (Legacy vs. New Eval)");
        log?.Invoke("====================================================================================");

        using var doc = baselineJsonPath != null && File.Exists(baselineJsonPath)
            ? JsonDocument.Parse(await File.ReadAllTextAsync(baselineJsonPath))
            : null;

        var fixedResults = new List<SearchSpeedVariantResult>(2);

        foreach (var variant in new[] { CheckersVariant.English, CheckersVariant.International })
        {
            string propName = variant == CheckersVariant.International ? "International" : "English";
            JsonElement? arr = doc?.RootElement.GetProperty(propName);

            var engine = new RuleEngine(variant);
            var legacyEval = new LegacyEvaluationFunction(variant);
            var newEval = new EvaluationFunction(variant);
            var ttLegacy = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);
            var ttNew = TranspositionTable.FromEntries(TranspositionTable.DefaultEntries);

            long legacyNoTtNodes = 0, newNoTtNodes = 0;
            long legacyTtNodes = 0, newTtNodes = 0;
            long legacyTtCutoffs = 0, newTtCutoffs = 0;

            var swLegacyNoTt = new Stopwatch();
            var swNewNoTt = new Stopwatch();
            var swLegacyTt = new Stopwatch();
            var swNewTt = new Stopwatch();

            for (int i = 0; i < positions.Count; i++)
            {
                int depth = arr.HasValue ? arr.Value[i].GetProperty("CalibratedDepth").GetInt32() : 9;
                var state = positions[i].State;
                var legalMoves = engine.GetLegalMoves(state);

                // 1. Legacy No-TT
                var pLegacyNoTt = new MinimaxPlayer(
                    SearchLimits.FixedDepth(depth),
                    ruleEngine: engine,
                    evaluator: legacyEval,
                    useTranspositionTable: false,
                    useQuiescence: true,
                    variant: variant);
                swLegacyNoTt.Start();
                await pLegacyNoTt.GetMoveAsync(state, legalMoves);
                swLegacyNoTt.Stop();
                legacyNoTtNodes += pLegacyNoTt.NodesEvaluated;

                // 2. New No-TT
                var pNewNoTt = new MinimaxPlayer(
                    SearchLimits.FixedDepth(depth),
                    ruleEngine: engine,
                    evaluator: newEval,
                    useTranspositionTable: false,
                    useQuiescence: true,
                    variant: variant);
                swNewNoTt.Start();
                await pNewNoTt.GetMoveAsync(state, legalMoves);
                swNewNoTt.Stop();
                newNoTtNodes += pNewNoTt.NodesEvaluated;

                // 3. Legacy 1M TT
                ttLegacy.Clear();
                var pLegacyTt = new MinimaxPlayer(
                    SearchLimits.FixedDepth(depth),
                    ruleEngine: engine,
                    evaluator: legacyEval,
                    transpositionTable: ttLegacy,
                    useTranspositionTable: true,
                    useQuiescence: true,
                    variant: variant);
                swLegacyTt.Start();
                await pLegacyTt.GetMoveAsync(state, legalMoves);
                swLegacyTt.Stop();
                legacyTtNodes += pLegacyTt.NodesEvaluated;
                legacyTtCutoffs += ttLegacy.Cutoffs;

                // 4. New 1M TT
                ttNew.Clear();
                var pNewTt = new MinimaxPlayer(
                    SearchLimits.FixedDepth(depth),
                    ruleEngine: engine,
                    evaluator: newEval,
                    transpositionTable: ttNew,
                    useTranspositionTable: true,
                    useQuiescence: true,
                    variant: variant);
                swNewTt.Start();
                await pNewTt.GetMoveAsync(state, legalMoves);
                swNewTt.Stop();
                newTtNodes += pNewTt.NodesEvaluated;
                newTtCutoffs += ttNew.Cutoffs;
            }

            var res = new SearchSpeedVariantResult(
                Variant: variant,
                LegacyNoTtNodes: legacyNoTtNodes,
                LegacyNoTtMs: swLegacyNoTt.ElapsedMilliseconds,
                LegacyNoTtNpsM: (legacyNoTtNodes / 1_000_000.0) / Math.Max(0.001, swLegacyNoTt.Elapsed.TotalSeconds),
                NewNoTtNodes: newNoTtNodes,
                NewNoTtMs: swNewNoTt.ElapsedMilliseconds,
                NewNoTtNpsM: (newNoTtNodes / 1_000_000.0) / Math.Max(0.001, swNewNoTt.Elapsed.TotalSeconds),
                LegacyTt1MNodes: legacyTtNodes,
                LegacyTt1MMs: swLegacyTt.ElapsedMilliseconds,
                LegacyTt1MNpsM: (legacyTtNodes / 1_000_000.0) / Math.Max(0.001, swLegacyTt.Elapsed.TotalSeconds),
                LegacyTt1MCutoffs: legacyTtCutoffs,
                NewTt1MNodes: newTtNodes,
                NewTt1MMs: swNewTt.ElapsedMilliseconds,
                NewTt1MNpsM: (newTtNodes / 1_000_000.0) / Math.Max(0.001, swNewTt.Elapsed.TotalSeconds),
                NewTt1MCutoffs: newTtCutoffs);

            fixedResults.Add(res);
            log?.Invoke($"\n--- {variant} (40 Positions Fixed-Depth) ---");
            log?.Invoke($"  No-TT Legacy : {res.LegacyNoTtNodes,11:N0} nodes | {res.LegacyNoTtMs,5} ms | {res.LegacyNoTtNpsM,6:F2}M nodes/s");
            log?.Invoke($"  No-TT New    : {res.NewNoTtNodes,11:N0} nodes | {res.NewNoTtMs,5} ms | {res.NewNoTtNpsM,6:F2}M nodes/s");
            log?.Invoke($"  1M TT Legacy : {res.LegacyTt1MNodes,11:N0} nodes | {res.LegacyTt1MMs,5} ms | {res.LegacyTt1MNpsM,6:F2}M nodes/s | Cutoffs: {res.LegacyTt1MCutoffs:N0}");
            log?.Invoke($"  1M TT New    : {res.NewTt1MNodes,11:N0} nodes | {res.NewTt1MMs,5} ms | {res.NewTt1MNpsM,6:F2}M nodes/s | Cutoffs: {res.NewTt1MCutoffs:N0}");
        }

        log?.Invoke("\n====================================================================================");
        log?.Invoke($"  PART C: 5-Position Timed Search Comparison ({timedSecondsPerPosition:F1}s Budget / Position, 16M TT)");
        log?.Invoke("====================================================================================");

        int[] fiveIndices = [0, 12, 19, 31, 39];
        var timedResults = new List<TimedPositionComparisonResult>(10);
        var timedTt = TranspositionTable.FromEntries(TranspositionTable.MaxEntries);

        foreach (var variant in new[] { CheckersVariant.English, CheckersVariant.International })
        {
            var engine = new RuleEngine(variant);
            var legacyEval = new LegacyEvaluationFunction(variant);
            var newEval = new EvaluationFunction(variant);

            log?.Invoke($"\n--- {variant} ---");
            foreach (int idx in fiveIndices)
            {
                var pos = positions[idx];
                var legalMoves = engine.GetLegalMoves(pos.State);

                timedTt.Clear();
                var pLegacy = new MinimaxPlayer(
                    SearchLimits.TimePerMove(TimeSpan.FromSeconds(timedSecondsPerPosition)),
                    ruleEngine: engine,
                    evaluator: legacyEval,
                    transpositionTable: timedTt,
                    useTranspositionTable: true,
                    useQuiescence: true,
                    variant: variant);
                var swL = Stopwatch.StartNew();
                var moveL = await pLegacy.GetMoveAsync(pos.State, legalMoves);
                swL.Stop();

                timedTt.Clear();
                var pNew = new MinimaxPlayer(
                    SearchLimits.TimePerMove(TimeSpan.FromSeconds(timedSecondsPerPosition)),
                    ruleEngine: engine,
                    evaluator: newEval,
                    transpositionTable: timedTt,
                    useTranspositionTable: true,
                    useQuiescence: true,
                    variant: variant);
                var swN = Stopwatch.StartNew();
                var moveN = await pNew.GetMoveAsync(pos.State, legalMoves);
                swN.Stop();

                var item = new TimedPositionComparisonResult(
                    Variant: variant,
                    PositionId: pos.Id,
                    Category: pos.Category,
                    LegacyDepth: pLegacy.LastAnalysis?.Depth ?? "-",
                    LegacyMove: moveL.Notation,
                    LegacyScore: pLegacy.LastAnalysis?.Value ?? "-",
                    LegacyNodes: pLegacy.NodesEvaluated,
                    LegacyTimeMs: swL.ElapsedMilliseconds,
                    LegacyNpsM: (pLegacy.NodesEvaluated / 1_000_000.0) / Math.Max(0.001, swL.Elapsed.TotalSeconds),
                    NewDepth: pNew.LastAnalysis?.Depth ?? "-",
                    NewMove: moveN.Notation,
                    NewScore: pNew.LastAnalysis?.Value ?? "-",
                    NewNodes: pNew.NodesEvaluated,
                    NewTimeMs: swN.ElapsedMilliseconds,
                    NewNpsM: (pNew.NodesEvaluated / 1_000_000.0) / Math.Max(0.001, swN.Elapsed.TotalSeconds));

                timedResults.Add(item);
                log?.Invoke(
                    $"  Pos #{pos.Id,2} ({pos.Category,-16}): " +
                    $"Legacy [{item.LegacyDepth,-9} {item.LegacyMove,-7} {item.LegacyScore,-5} {item.LegacyNpsM,5:F2}M/s] vs " +
                    $"New [{item.NewDepth,-9} {item.NewMove,-7} {item.NewScore,-5} {item.NewNpsM,5:F2}M/s]");
            }
        }

        return new EvaluationSpeedBenchmarkReport(microResults, fixedResults, timedResults);
    }

    /// <summary>
    /// Runs a 50-position (100-game color-swapped) head-to-head match between <see cref="EvaluationFunction"/>
    /// and <see cref="LegacyEvaluationFunction"/> with complete per-engine data structure isolation.
    /// </summary>
    public static async Task<VariantMatchSummary> RunMatchAsync(
        CheckersVariant variant,
        int ballotCount = 50,
        int timePerMoveMs = 1000,
        int workers = 4,
        Action<int, int, MatchGameResult>? progressCallback = null)
    {
        var ballots = GenerateBalancedBallots(variant, ballotCount);
        int totalGames = ballots.Count * 2;

        // Build the 100 game descriptors (2 per ballot: Game A = New White vs Legacy Black, Game B = Legacy White vs New Black)
        var specs = new List<(int GameId, OpeningBallot Ballot, bool NewEvalIsWhite)>(totalGames);
        for (int i = 0; i < ballots.Count; i++)
        {
            specs.Add(((i * 2) + 1, ballots[i], true));
            specs.Add(((i * 2) + 2, ballots[i], false));
        }

        var results = new ConcurrentBag<MatchGameResult>();
        int completedCount = 0;
        var matchSw = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            specs,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, workers) },
            async (spec, ct) =>
            {
                var gameResult = await PlayIsolatedGameAsync(
                    variant,
                    spec.GameId,
                    spec.Ballot,
                    spec.NewEvalIsWhite,
                    timePerMoveMs,
                    ct);

                results.Add(gameResult);
                int done = Interlocked.Increment(ref completedCount);
                progressCallback?.Invoke(done, totalGames, gameResult);
            });

        matchSw.Stop();

        var orderedGames = results.OrderBy(r => r.GameId).ToList();
        return SummarizeMatch(variant, timePerMoveMs, workers, ballots.Count, orderedGames, matchSw.Elapsed.TotalSeconds);
    }

    private static async Task<MatchGameResult> PlayIsolatedGameAsync(
        CheckersVariant variant,
        int gameId,
        OpeningBallot ballot,
        bool newEvalIsWhite,
        int timePerMoveMs,
        CancellationToken ct)
    {
        var ruleEngine = new RuleEngine(variant);

        // Complete Per-Engine Data Structure Isolation:
        // Separate TranspositionTable (32 MiB = 2,097,152 entries) and separate MinimaxPlayer per side
        var whiteTt = new TranspositionTable(32);
        var blackTt = new TranspositionTable(32);

        IEvaluationFunction whiteEval = newEvalIsWhite
            ? new EvaluationFunction(variant)
            : new LegacyEvaluationFunction(variant);

        IEvaluationFunction blackEval = newEvalIsWhite
            ? new LegacyEvaluationFunction(variant)
            : new EvaluationFunction(variant);

        var limits = SearchLimits.TimePerMove(TimeSpan.FromMilliseconds(timePerMoveMs));

        var whitePlayer = new MinimaxPlayer(
            limits,
            ruleEngine: ruleEngine,
            evaluator: whiteEval,
            transpositionTable: whiteTt,
            useTranspositionTable: true,
            useQuiescence: true,
            variant: variant);

        var blackPlayer = new MinimaxPlayer(
            limits,
            ruleEngine: ruleEngine,
            evaluator: blackEval,
            transpositionTable: blackTt,
            useTranspositionTable: true,
            useQuiescence: true,
            variant: variant);

        var state = ballot.State.Clone();
        var history = new List<ulong>(256);
        history.AddRange(ballot.InitialHashHistory);

        int pliesPlayed = 0;
        const int maxPlies = 200;

        long newDepthSum = 0, newMoveCount = 0;
        long legacyDepthSum = 0, legacyMoveCount = 0;
        long newNodesSum = 0, legacyNodesSum = 0;
        double newTimeSecSum = 0.0, legacyTimeSecSum = 0.0;

        string terminationReason = "MaxPliesDraw";
        int newEvalPointsHalf = 1; // Default Draw (1 half-point)

        while (pliesPlayed < maxPlies)
        {
            var (status, reason) = ruleEngine.EvaluateGameStatus(state, history);
            if (status != GameStatus.InProgress)
            {
                if (status == GameStatus.WhiteWon || status == GameStatus.BlackWon)
                {
                    bool whiteWon = status == GameStatus.WhiteWon;
                    bool newWon = (whiteWon && newEvalIsWhite) || (!whiteWon && !newEvalIsWhite);
                    newEvalPointsHalf = newWon ? 2 : 0;
                    terminationReason = $"{status} ({reason})";
                }
                else
                {
                    newEvalPointsHalf = 1;
                    terminationReason = $"Draw ({reason})";
                }
                break;
            }

            var legalMoves = ruleEngine.GetLegalMoves(state);
            bool isWhiteTurn = state.ActivePlayer == PieceColor.White;
            var activePlayer = isWhiteTurn ? whitePlayer : blackPlayer;
            bool isNewTurn = (isWhiteTurn && newEvalIsWhite) || (!isWhiteTurn && !newEvalIsWhite);

            var sw = Stopwatch.StartNew();
            var chosenMove = await activePlayer.GetMoveAsync(state, legalMoves, progress: null, stateHashHistory: history, cancellationToken: ct);
            sw.Stop();

            if (legalMoves.Count > 1)
            {
                int depthReached = ParseDepth(activePlayer.LastAnalysis?.Depth);
                if (isNewTurn)
                {
                    newDepthSum += depthReached;
                    newMoveCount++;
                    newNodesSum += activePlayer.NodesEvaluated;
                    newTimeSecSum += sw.Elapsed.TotalSeconds;
                }
                else
                {
                    legacyDepthSum += depthReached;
                    legacyMoveCount++;
                    legacyNodesSum += activePlayer.NodesEvaluated;
                    legacyTimeSecSum += sw.Elapsed.TotalSeconds;
                }
            }

            state = ruleEngine.ApplyMove(state, chosenMove);
            history.Add(state.ZobristHash);
            pliesPlayed++;
        }

        return new MatchGameResult(
            GameId: gameId,
            BallotId: ballot.BallotId,
            NewEvalIsWhite: newEvalIsWhite,
            NewEvalPointsHalf: newEvalPointsHalf,
            TerminationReason: terminationReason,
            TotalPlies: pliesPlayed,
            AvgNewDepth: newMoveCount > 0 ? (double)newDepthSum / newMoveCount : 0.0,
            AvgLegacyDepth: legacyMoveCount > 0 ? (double)legacyDepthSum / legacyMoveCount : 0.0,
            AvgNewNpsMillions: newTimeSecSum > 0.001 ? (newNodesSum / 1_000_000.0) / newTimeSecSum : 0.0,
            AvgLegacyNpsMillions: legacyTimeSecSum > 0.001 ? (legacyNodesSum / 1_000_000.0) / legacyTimeSecSum : 0.0);
    }

    private static VariantMatchSummary SummarizeMatch(
        CheckersVariant variant,
        int timePerMoveMs,
        int workers,
        int ballotCount,
        IReadOnlyList<MatchGameResult> games,
        double wallTimeSec)
    {
        int newWins = games.Count(g => g.NewEvalPointsHalf == 2);
        int draws = games.Count(g => g.NewEvalPointsHalf == 1);
        int legacyWins = games.Count(g => g.NewEvalPointsHalf == 0);

        int newWhiteWins = games.Count(g => g.NewEvalIsWhite && g.NewEvalPointsHalf == 2);
        int newWhiteDraws = games.Count(g => g.NewEvalIsWhite && g.NewEvalPointsHalf == 1);
        int newWhiteLosses = games.Count(g => g.NewEvalIsWhite && g.NewEvalPointsHalf == 0);

        int newBlackWins = games.Count(g => !g.NewEvalIsWhite && g.NewEvalPointsHalf == 2);
        int newBlackDraws = games.Count(g => !g.NewEvalIsWhite && g.NewEvalPointsHalf == 1);
        int newBlackLosses = games.Count(g => !g.NewEvalIsWhite && g.NewEvalPointsHalf == 0);

        int sweepsWin = 0, miniWins = 0, splits = 0, miniLosses = 0, sweepsLoss = 0;
        for (int b = 1; b <= ballotCount; b++)
        {
            int ballotHalfPoints = games.Where(g => g.BallotId == b).Sum(g => g.NewEvalPointsHalf);
            switch (ballotHalfPoints)
            {
                case 4: sweepsWin++; break;   // 2.0 - 0.0
                case 3: miniWins++; break;    // 1.5 - 0.5
                case 2: splits++; break;      // 1.0 - 1.0
                case 1: miniLosses++; break;  // 0.5 - 1.5
                case 0: sweepsLoss++; break;  // 0.0 - 2.0
            }
        }

        int n = games.Count;
        double score = newWins + (0.5 * draws);
        double mu = Math.Clamp(score / n, 0.005, 0.995);
        double eloDiff = -400.0 * Math.Log10((1.0 / mu) - 1.0);

        // Compute sample standard deviation of game outcomes {1.0, 0.5, 0.0}
        double variance = (
            (newWins * Math.Pow(1.0 - mu, 2)) +
            (draws * Math.Pow(0.5 - mu, 2)) +
            (legacyWins * Math.Pow(0.0 - mu, 2))
        ) / Math.Max(1, n - 1);

        double stdErr = Math.Sqrt(variance / n);
        double muLow = Math.Clamp(mu - (1.96 * stdErr), 0.001, 0.999);
        double muHigh = Math.Clamp(mu + (1.96 * stdErr), 0.001, 0.999);
        double eloLow = -400.0 * Math.Log10((1.0 / muLow) - 1.0);
        double eloHigh = -400.0 * Math.Log10((1.0 / muHigh) - 1.0);
        double eloError95 = (eloHigh - eloLow) / 2.0;

        // Likelihood of Superiority (LOS)
        double z = stdErr > 1e-9 ? (mu - 0.5) / stdErr : 0.0;
        double los = 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0))) * 100.0;

        return new VariantMatchSummary(
            Variant: variant,
            TimePerMoveMs: timePerMoveMs,
            Workers: workers,
            TotalGames: n,
            NewEvalWins: newWins,
            Draws: draws,
            LegacyEvalWins: legacyWins,
            NewWhiteWins: newWhiteWins,
            NewWhiteDraws: newWhiteDraws,
            NewWhiteLosses: newWhiteLosses,
            NewBlackWins: newBlackWins,
            NewBlackDraws: newBlackDraws,
            NewBlackLosses: newBlackLosses,
            BallotSweepsWin: sweepsWin,
            BallotMiniWins: miniWins,
            BallotSplits: splits,
            BallotMiniLosses: miniLosses,
            BallotSweepsLoss: sweepsLoss,
            ScorePercentage: mu * 100.0,
            EloDifference: eloDiff,
            EloError95: eloError95,
            LikelihoodOfSuperiority: los,
            AvgPliesPerGame: games.Average(g => g.TotalPlies),
            AvgNewEvalDepth: games.Where(g => g.AvgNewDepth > 0).Select(g => g.AvgNewDepth).DefaultIfEmpty(0).Average(),
            AvgLegacyEvalDepth: games.Where(g => g.AvgLegacyDepth > 0).Select(g => g.AvgLegacyDepth).DefaultIfEmpty(0).Average(),
            AvgNewEvalNpsMillions: games.Where(g => g.AvgNewNpsMillions > 0).Select(g => g.AvgNewNpsMillions).DefaultIfEmpty(0).Average(),
            AvgLegacyEvalNpsMillions: games.Where(g => g.AvgLegacyNpsMillions > 0).Select(g => g.AvgLegacyNpsMillions).DefaultIfEmpty(0).Average(),
            TotalWallTimeSeconds: wallTimeSec,
            Games: games);
    }

    public static string FormatMatchSummaryMarkdown(VariantMatchSummary s)
    {
        var sb = new StringBuilder();
        double points = s.NewEvalWins + (0.5 * s.Draws);
        double legacyPoints = s.LegacyEvalWins + (0.5 * s.Draws);

        sb.AppendLine($"### {s.Variant} Checkers — 100-Game Match (`NewEval` vs. `LegacyEval`, {s.TimePerMoveMs} ms/move)");
        sb.AppendLine();
        sb.AppendLine("| Metric | `NewEval` (`EvaluationFunction`) | `LegacyEval` (`LegacyEvaluationFunction`) |");
        sb.AppendLine("| :--- | :---: | :---: |");
        sb.AppendLine($"| **Total Score (100 Games)** | **{points:F1} / {s.TotalGames}** (**{s.ScorePercentage:F1}%**) | {legacyPoints:F1} / {s.TotalGames} ({100.0 - s.ScorePercentage:F1}%) |");
        sb.AppendLine($"| **Wins / Draws / Losses** | **+{s.NewEvalWins} = {s.Draws} -{s.LegacyEvalWins}** | +{s.LegacyEvalWins} = {s.Draws} -{s.NewEvalWins} |");
        sb.AppendLine($"| **As White (50 Games)** | +{s.NewWhiteWins} = {s.NewWhiteDraws} -{s.NewWhiteLosses} | +{s.NewBlackLosses} = {s.NewBlackDraws} -{s.NewBlackWins} |");
        sb.AppendLine($"| **As Black (50 Games)** | +{s.NewBlackWins} = {s.NewBlackDraws} -{s.NewBlackLosses} | +{s.NewWhiteLosses} = {s.NewWhiteDraws} -{s.NewWhiteWins} |");
        sb.AppendLine($"| **Paired 50-Ballot Breakdown (`2-0` / `1.5-0.5` / `1-1` / `0.5-1.5` / `0-2`)** | `{s.BallotSweepsWin}` / `{s.BallotMiniWins}` / `{s.BallotSplits}` / `{s.BallotMiniLosses}` / `{s.BallotSweepsLoss}` | `{s.BallotSweepsLoss}` / `{s.BallotMiniLosses}` / `{s.BallotSplits}` / `{s.BallotMiniWins}` / `{s.BallotSweepsWin}` |");
        sb.AppendLine($"| **Elo Difference ($\\Delta\\text{{Elo}}$ ± 95% CI)** | **{s.EloDifference:+0.0;-0.0;0.0} ± {s.EloError95:F1} Elo** (LOS: **{s.LikelihoodOfSuperiority:F1}%**) | {-s.EloDifference:+0.0;-0.0;0.0} ± {s.EloError95:F1} Elo |");
        sb.AppendLine($"| **Average Search Depth** | `{s.AvgNewEvalDepth:F2} plies` | `{s.AvgLegacyEvalDepth:F2} plies` |");
        sb.AppendLine($"| **Average Search Speed (NPS)** | `{s.AvgNewEvalNpsMillions:F2}M nodes/s` | `{s.AvgLegacyEvalNpsMillions:F2}M nodes/s` |");
        sb.AppendLine($"| **Average Game Length** | `{s.AvgPliesPerGame:F1} plies` (Wall time: `{s.TotalWallTimeSeconds:F1}s`) | — |");
        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long RunRawLegacyLoop(BitPosition[] positions, CheckersVariant variant, int passes)
    {
        long acc = 0;
        for (int p = 0; p < passes; p++)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                acc += LegacyEvaluationFunction.Evaluate(in positions[i], variant);
            }
        }
        return acc;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long RunRawNewLoop(BitPosition[] positions, CheckersVariant variant, int passes)
    {
        long acc = 0;
        for (int p = 0; p < passes; p++)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                acc += EvaluationFunction.Evaluate(in positions[i], variant);
            }
        }
        return acc;
    }

    private static int ParseScore(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        if (int.TryParse(value.Replace("+", ""), out int parsed))
            return parsed;
        return 0;
    }

    private static int ParseDepth(string? depthText)
    {
        if (string.IsNullOrWhiteSpace(depthText))
            return 0;
        var parts = depthText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && int.TryParse(parts[0], out int depth))
            return depth;
        return 0;
    }

    private static double Erf(double x)
    {
        // Abramowitz and Stegun formula 7.1.26
        double sign = x < 0 ? -1.0 : 1.0;
        x = Math.Abs(x);
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        double t = 1.0 / (1.0 + (p * x));
        double y = 1.0 - (((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x));
        return sign * y;
    }
}
