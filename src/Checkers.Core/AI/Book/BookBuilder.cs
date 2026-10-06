using System.Collections.Concurrent;
using System.Diagnostics;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.AI.Book;

/// <summary>
/// Represents a directed edge from a parent book position to a child book position via a specific <see cref="Move"/>.
/// </summary>
public readonly record struct BookEdge(Move Move, ulong ChildHash);

/// <summary>
/// Represents a unique position discovered during breadth-first opening book enumeration.
/// </summary>
public sealed record BookDagPosition(
    ulong ZobristHash,
    int Ply,
    BoardState State,
    IReadOnlyList<BookEdge> Edges)
{
    public bool IsLeaf => Edges.Count == 0;
}

/// <summary>
/// Directed Acyclic Graph (DAG) of unique opening positions up to <see cref="Depth"/> plies with up to
/// <see cref="Width"/> candidate moves expanded per internal node.
/// Transpositions reaching the same <see cref="BoardState.ZobristHash"/> are merged into a single node.
/// </summary>
public sealed class BookDag
{
    public CheckersVariant Variant { get; }
    public int Depth { get; }
    public int Width { get; }
    public IReadOnlyList<IReadOnlyList<BookDagPosition>> Levels { get; }
    public IReadOnlyDictionary<ulong, BookDagPosition> AllPositions { get; }
    public IReadOnlyList<BookDagPosition> Leaves { get; }

    public int TotalPositions => AllPositions.Count;
    public int InternalPositionsCount => TotalPositions - Leaves.Count;

    public BookDag(
        CheckersVariant variant,
        int depth,
        int width,
        IReadOnlyList<IReadOnlyList<BookDagPosition>> levels,
        IReadOnlyDictionary<ulong, BookDagPosition> allPositions)
    {
        Variant = variant;
        Depth = depth;
        Width = width;
        Levels = levels;
        AllPositions = allPositions;
        Leaves = levels
            .SelectMany(l => l)
            .Where(p => p.IsLeaf)
            .OrderBy(p => p.Ply)
            .ThenBy(p => p.ZobristHash)
            .ToList();
    }
}

/// <summary>
/// Reports progress for a single evaluated (or checkpoint-restored) position during
/// <see cref="BookBuilder.GenerateTopDown"/>.
/// </summary>
public readonly record struct TopDownNodeProgress(
    BookNode EvaluatedNode,
    int Level,
    int CompletedInLevel,
    int TotalInLevel,
    int TotalEvaluatedOverall,
    int CompletedDepth,
    long NodesEvaluated,
    bool FromCheckpoint);

/// <summary>
/// Reports progress for a single evaluated or widened position during
/// <see cref="BookBuilder.ExpandDropOut"/>.
/// </summary>
public readonly record struct DoeExpansionProgress(
    BookNode EvaluatedNode,
    string Phase,
    int CompletedInPhase,
    int? TotalInPhase,
    int TotalInternalNodes,
    int TotalBookNodes,
    int ActiveFrontierLeaves,
    int CompletedDepth,
    long NodesEvaluated,
    bool FromCheckpoint);

/// <summary>
/// Builds and verifies transposition-aware Checkers opening books using Top-Down Level-by-Level
/// Multi-PV search (<see cref="GenerateTopDown"/>), Drop-Out Expansion (<see cref="ExpandDropOut"/>),
/// or breadth-first DAG enumeration followed by bottom-up Negamax score back-up (<see cref="BackUp"/>).
/// </summary>
public static class BookBuilder
{
    public const int MaxDepth = 32;
    public const int MaxWidth = 32;
    public const int DefaultMaxEvaluatedLevel = 7;
    public const int DefaultDepth = 7;
    public const int DefaultWidth = 3;
    public const int DefaultFullWidthPlies = 4;
    public const int DefaultMaxPly = 12;
    public const int DefaultSelectDepth = 12;
    public const int DefaultWorkers = 8;

    /// <summary>
    /// Returns the drop-out threshold <c>delta</c> (in centipawns) for a position at <paramref name="ply"/>.
    /// Moves whose backed-up score trails the node's best move by more than <c>delta</c> drop out of DOE expansion.
    /// Defaults to a depth-tapered schedule (<c>15 cp</c> for <c>lv 0..5</c>, <c>10 cp</c> for <c>lv 6..9</c>,
    /// and <c>6 cp</c> for <c>lv 10+</c>) unless overridden by <paramref name="fixedDeltaCp"/>.
    /// </summary>
    public static int GetDefaultDropOutDelta(int ply, int? fixedDeltaCp = null) =>
        fixedDeltaCp ?? (ply <= 5 ? 15 : ply <= 9 ? 10 : 6);

    /// <summary>
    /// Enumerates the opening DAG up to <paramref name="depth"/> plies, expanding at most <paramref name="width"/>
    /// moves per internal position using <paramref name="selectTopMoves"/> and merging transpositions by
    /// <see cref="BoardState.ZobristHash"/>.
    /// </summary>
    public static BookDag Enumerate(
        CheckersVariant variant,
        int depth,
        int width,
        Func<BoardState, IReadOnlyList<Move>, IReadOnlyList<Move>> selectTopMoves,
        Action<int, int, int>? onLevelComplete = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, MaxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxWidth);
        ArgumentNullException.ThrowIfNull(selectTopMoves);

        var ruleEngine = new RuleEngine(variant);
        var levels = new List<IReadOnlyList<BookDagPosition>>(depth + 1);
        var allPositions = new Dictionary<ulong, BookDagPosition>();

        var initial = BoardState.CreateInitial();
        var currentStates = new List<BoardState> { initial };
        var reservedHashes = new HashSet<ulong> { initial.ZobristHash };

        for (int ply = 0; ply <= depth; ply++)
        {
            var levelPositions = new List<BookDagPosition>(currentStates.Count);
            var nextStates = new List<BoardState>();

            if (ply == depth)
            {
                foreach (var state in currentStates)
                {
                    var node = new BookDagPosition(state.ZobristHash, ply, state, []);
                    levelPositions.Add(node);
                    allPositions[node.ZobristHash] = node;
                }
            }
            else
            {
                foreach (var state in currentStates)
                {
                    var legalMoves = ruleEngine.GetLegalMoves(state);
                    if (legalMoves.Count == 0)
                    {
                        var terminalNode = new BookDagPosition(state.ZobristHash, ply, state, []);
                        levelPositions.Add(terminalNode);
                        allPositions[terminalNode.ZobristHash] = terminalNode;
                        continue;
                    }

                    IReadOnlyList<Move> chosenMoves = legalMoves.Count <= width
                        ? legalMoves
                        : selectTopMoves(state, legalMoves);

                    var edges = new List<BookEdge>(Math.Min(width, chosenMoves.Count));
                    var seenEdgeTargets = new HashSet<ulong>();

                    foreach (var move in chosenMoves.Take(width))
                    {
                        var childState = ruleEngine.ApplyMove(state, move);
                        ulong childHash = childState.ZobristHash;

                        if (seenEdgeTargets.Add(childHash))
                        {
                            edges.Add(new BookEdge(move, childHash));
                        }

                        if (reservedHashes.Add(childHash))
                        {
                            nextStates.Add(childState);
                        }
                    }

                    var dagNode = new BookDagPosition(state.ZobristHash, ply, state, edges);
                    levelPositions.Add(dagNode);
                    allPositions[dagNode.ZobristHash] = dagNode;
                }
            }

            levels.Add(levelPositions);
            onLevelComplete?.Invoke(ply, levelPositions.Count, allPositions.Count);
            currentStates = nextStates;
        }

        return new BookDag(variant, depth, width, levels, allPositions);
    }

    /// <summary>
    /// Enumerates the opening DAG using a fixed-depth <see cref="MinimaxPlayer"/> screening search
    /// (<paramref name="selectDepth"/> plies) to rank legal moves at each internal node in parallel.
    /// </summary>
    public static BookDag EnumerateWithSearch(
        CheckersVariant variant,
        int depth = DefaultDepth,
        int width = DefaultWidth,
        int selectDepth = DefaultSelectDepth,
        int workers = 1,
        Action<int, int, int>? onLevelComplete = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, MaxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxWidth);
        ArgumentOutOfRangeException.ThrowIfLessThan(selectDepth, 1);

        var ruleEngine = new RuleEngine(variant);
        var levels = new List<IReadOnlyList<BookDagPosition>>(depth + 1);
        var allPositions = new Dictionary<ulong, BookDagPosition>();

        var initial = BoardState.CreateInitial();
        var currentStates = new List<BoardState> { initial };
        var reservedHashes = new HashSet<ulong> { initial.ZobristHash };

        int maxDegree = Math.Max(1, workers);

        for (int ply = 0; ply <= depth; ply++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ply == depth)
            {
                var leafPositions = new List<BookDagPosition>(currentStates.Count);
                foreach (var state in currentStates)
                {
                    var node = new BookDagPosition(state.ZobristHash, ply, state, []);
                    leafPositions.Add(node);
                    allPositions[node.ZobristHash] = node;
                }

                levels.Add(leafPositions);
                onLevelComplete?.Invoke(ply, leafPositions.Count, allPositions.Count);
                break;
            }

            var selectedMovesPerState = new IReadOnlyList<Move>[currentStates.Count];
            var partitioner = Partitioner.Create(0, currentStates.Count);

            Parallel.ForEach(
                partitioner,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegree,
                    CancellationToken = cancellationToken
                },
                range =>
                {
                    var localEngine = new RuleEngine(variant);
                    var localEval = new EvaluationFunction(variant);
                    var localTt = new TranspositionTable(megabytes: 16);
                    int childSearchDepth = Math.Max(1, selectDepth - 1);
                    var localPlayer = new MinimaxPlayer(
                        SearchLimits.FixedDepth(childSearchDepth),
                        ruleEngine: localEngine,
                        evaluator: localEval,
                        transpositionTable: localTt,
                        useTranspositionTable: true,
                        useQuiescence: true,
                        variant: variant);

                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var state = currentStates[i];
                        var legalMoves = localEngine.GetLegalMoves(state);
                        if (legalMoves.Count <= width)
                        {
                            selectedMovesPerState[i] = legalMoves;
                        }
                        else
                        {
                            selectedMovesPerState[i] = RankAndSelectMoves(
                                state,
                                legalMoves,
                                width,
                                localEngine,
                                localEval,
                                localPlayer,
                                cancellationToken);
                        }
                    }
                });

            var levelPositions = new List<BookDagPosition>(currentStates.Count);
            var nextStates = new List<BoardState>();

            for (int i = 0; i < currentStates.Count; i++)
            {
                var state = currentStates[i];
                var chosenMoves = selectedMovesPerState[i];
                if (chosenMoves.Count == 0)
                {
                    var terminalNode = new BookDagPosition(state.ZobristHash, ply, state, []);
                    levelPositions.Add(terminalNode);
                    allPositions[terminalNode.ZobristHash] = terminalNode;
                    continue;
                }

                var edges = new List<BookEdge>(Math.Min(width, chosenMoves.Count));
                var seenEdgeTargets = new HashSet<ulong>();

                foreach (var move in chosenMoves.Take(width))
                {
                    var childState = ruleEngine.ApplyMove(state, move);
                    ulong childHash = childState.ZobristHash;

                    if (seenEdgeTargets.Add(childHash))
                    {
                        edges.Add(new BookEdge(move, childHash));
                    }

                    if (reservedHashes.Add(childHash))
                    {
                        nextStates.Add(childState);
                    }
                }

                var dagNode = new BookDagPosition(state.ZobristHash, ply, state, edges);
                levelPositions.Add(dagNode);
                allPositions[dagNode.ZobristHash] = dagNode;
            }

            levels.Add(levelPositions);
            onLevelComplete?.Invoke(ply, levelPositions.Count, allPositions.Count);
            currentStates = nextStates;
        }

        return new BookDag(variant, depth, width, levels, allPositions);
    }

    /// <summary>
    /// Evaluates a single leaf position using <paramref name="player"/> and returns the centipawn score
    /// from the leaf's <c>SideToMove</c> perspective.
    /// </summary>
    public static int EvaluateLeafPosition(
        BoardState leafState,
        RuleEngine ruleEngine,
        IEvaluationFunction evaluator,
        MinimaxPlayer player,
        CancellationToken cancellationToken = default)
    {
        var legalMoves = ruleEngine.GetLegalMoves(leafState);
        if (legalMoves.Count == 0)
        {
            return -BookFile.MaxScore;
        }

        if (legalMoves.Count == 1)
        {
            // When the only legal move is forced (e.g. a mandatory jump), evaluate the resulting state
            // and negate so the score reflects the position after the forced response.
            var afterForced = ruleEngine.ApplyMove(leafState, legalMoves[0]);
            var nextMoves = ruleEngine.GetLegalMoves(afterForced);
            if (nextMoves.Count == 0)
            {
                return BookFile.MaxScore;
            }

            player.GetMoveAsync(afterForced, nextMoves, cancellationToken).GetAwaiter().GetResult();
            return Math.Clamp(-ParseAnalysisScore(player.LastAnalysis?.Value, afterForced, evaluator), BookFile.MinScore, BookFile.MaxScore);
        }

        player.GetMoveAsync(leafState, legalMoves, cancellationToken).GetAwaiter().GetResult();
        return Math.Clamp(ParseAnalysisScore(player.LastAnalysis?.Value, leafState, evaluator), BookFile.MinScore, BookFile.MaxScore);
    }

    /// <summary>
    /// Backs up leaf scores from <c>ply == Depth</c> down to <c>ply == 0</c> by Negamax over the DAG,
    /// computing the backed-up score for every internal position and each of its stored candidate moves.
    /// </summary>
    public static List<BookNode> BackUp(BookDag dag, IReadOnlyDictionary<ulong, int> leafScores)
    {
        ArgumentNullException.ThrowIfNull(dag);
        ArgumentNullException.ThrowIfNull(leafScores);

        var nodeScores = new Dictionary<ulong, int>(dag.TotalPositions);
        var builtNodes = new Dictionary<ulong, BookNode>(dag.TotalPositions);

        foreach (var leaf in dag.Leaves)
        {
            if (!leafScores.TryGetValue(leaf.ZobristHash, out int score))
            {
                throw new ArgumentException(
                    $"Missing score for leaf position {leaf.ZobristHash:X16} at ply {leaf.Ply}.",
                    nameof(leafScores));
            }

            nodeScores[leaf.ZobristHash] = score;
            builtNodes[leaf.ZobristHash] = new BookNode(leaf.ZobristHash, leaf.Ply, score, []);
        }

        foreach (var pos in dag.AllPositions.Values)
        {
            if (!pos.IsLeaf)
            {
                nodeScores.TryAdd(pos.ZobristHash, 0);
            }
        }

        // Propagate bottom-up from ply = Depth - 1 down to 0 (with a second pass in case any cross-ply transposition exists).
        for (int pass = 0; pass < 2; pass++)
        {
            for (int ply = dag.Depth - 1; ply >= 0; ply--)
            {
                foreach (var dagPos in dag.Levels[ply])
                {
                    if (dagPos.IsLeaf)
                    {
                        continue;
                    }

                    var moveEntries = new List<BookMoveEntry>(dagPos.Edges.Count);
                    foreach (var edge in dagPos.Edges)
                    {
                        if (!nodeScores.TryGetValue(edge.ChildHash, out int childScore))
                        {
                            throw new InvalidOperationException(
                                $"Child position {edge.ChildHash:X16} of {dagPos.ZobristHash:X16} (move {edge.Move.Notation}) has no score.");
                        }

                        int moveScore = Math.Clamp(-childScore, BookFile.MinScore, BookFile.MaxScore);
                        moveEntries.Add(new BookMoveEntry(edge.Move.Notation, moveScore));
                    }

                    var sortedMoves = moveEntries
                        .OrderByDescending(m => m.Score)
                        .ThenBy(m => m.Notation, StringComparer.Ordinal)
                        .ToList();

                    int bestScore = sortedMoves[0].Score;
                    nodeScores[dagPos.ZobristHash] = bestScore;
                    builtNodes[dagPos.ZobristHash] = new BookNode(dagPos.ZobristHash, dagPos.Ply, bestScore, sortedMoves);
                }
            }
        }

        var orderedResult = new List<BookNode>(builtNodes.Count);
        for (int ply = 0; ply <= dag.Depth; ply++)
        {
            foreach (var dagPos in dag.Levels[ply])
            {
                orderedResult.Add(builtNodes[dagPos.ZobristHash]);
            }
        }

        return orderedResult;
    }

    /// <summary>
    /// Generates a Checkers opening book top-down level by level (<c>lv 0</c> through <paramref name="maxEvaluatedLevel"/>),
    /// running a Single-Search Multi-PV (<paramref name="width"/>) search on each unique position at <c>lv p</c>,
    /// deduplicating transposed child states by <see cref="BoardState.ZobristHash"/>, and propagating the deepest
    /// evaluated level's scores bottom-up via <see cref="BackUp"/> after each level completes.
    /// </summary>
    public static List<BookNode> GenerateTopDown(
        CheckersVariant variant,
        int maxEvaluatedLevel = DefaultMaxEvaluatedLevel,
        int width = DefaultWidth,
        SearchLimits? nodeSearchLimits = null,
        int workers = DefaultWorkers,
        IDictionary<ulong, BookNode>? checkpointNodes = null,
        Action<TopDownNodeProgress>? onNodeEvaluated = null,
        Action<int, IReadOnlyList<BookNode>>? onLevelComplete = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEvaluatedLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxEvaluatedLevel, MaxDepth - 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxWidth);

        var limits = nodeSearchLimits ?? SearchLimits.TimePerMove(TimeSpan.FromSeconds(60), useOpeningBook: false);
        var ruleEngine = new RuleEngine(variant);
        var levels = new List<IReadOnlyList<BookDagPosition>>(maxEvaluatedLevel + 2);
        var allPositions = new Dictionary<ulong, BookDagPosition>();
        var directEvaluatedNodes = new Dictionary<ulong, BookNode>();

        var initial = BoardState.CreateInitial();
        var currentStates = new List<BoardState> { initial };
        var reservedHashes = new HashSet<ulong> { initial.ZobristHash };

        int totalEvaluatedOverall = 0;
        int maxDegree = Math.Max(1, workers);
        object syncLock = new();
        List<BookNode> latestBackedUpBook = [];

        for (int level = 0; level <= maxEvaluatedLevel; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int totalInLevel = currentStates.Count;
            int completedInLevel = 0;
            var statesToSearch = new List<BoardState>(totalInLevel);

            foreach (var state in currentStates)
            {
                var legalMoves = ruleEngine.GetLegalMoves(state);
                if (legalMoves.Count == 0)
                {
                    var terminalNode = new BookNode(state.ZobristHash, level, -BookFile.MaxScore, []);
                    directEvaluatedNodes[state.ZobristHash] = terminalNode;
                    completedInLevel++;
                    totalEvaluatedOverall++;
                    onNodeEvaluated?.Invoke(new TopDownNodeProgress(
                        terminalNode,
                        level,
                        completedInLevel,
                        totalInLevel,
                        totalEvaluatedOverall,
                        CompletedDepth: 0,
                        NodesEvaluated: 0,
                        FromCheckpoint: true));
                    continue;
                }

                int expectedMoves = Math.Min(width, legalMoves.Count);
                if (checkpointNodes != null
                    && checkpointNodes.TryGetValue(state.ZobristHash, out var cachedNode)
                    && cachedNode.Moves.Count >= expectedMoves
                    && cachedNode.Moves.Take(expectedMoves).All(m =>
                        legalMoves.Any(lm => string.Equals(lm.Notation, m.Notation, StringComparison.OrdinalIgnoreCase))))
                {
                    var trimmedMoves = cachedNode.Moves.Take(expectedMoves).ToList();
                    var restoredNode = new BookNode(
                        state.ZobristHash,
                        level,
                        trimmedMoves[0].Score,
                        trimmedMoves);

                    directEvaluatedNodes[state.ZobristHash] = restoredNode;
                    completedInLevel++;
                    totalEvaluatedOverall++;
                    onNodeEvaluated?.Invoke(new TopDownNodeProgress(
                        restoredNode,
                        level,
                        completedInLevel,
                        totalInLevel,
                        totalEvaluatedOverall,
                        CompletedDepth: 0,
                        NodesEvaluated: 0,
                        FromCheckpoint: true));
                }
                else
                {
                    statesToSearch.Add(state);
                }
            }

            if (statesToSearch.Count > 0)
            {
                Parallel.ForEach(
                    statesToSearch,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Min(maxDegree, statesToSearch.Count),
                        CancellationToken = cancellationToken
                    },
                    () => new TopDownWorkerContext(variant, limits),
                    (state, _, ctx) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var legalMoves = ctx.RuleEngine.GetLegalMoves(state);
                        BookNode evaluatedNode;
                        int completedDepth;
                        long nodesEvaluated;

                        if (legalMoves.Count == 1 && level < maxEvaluatedLevel && limits.Mode != TimeControlMode.FixedDepth)
                        {
                            // Single forced move (mandatory jump) at an internal level: move choice is 100% forced
                            // and its final score is backed up from maxEvaluatedLevel, so score quickly without wasting 60s.
                            var quickMultiPv = ctx.QuickForcedPlayer.SearchMultiPv(state, legalMoves, 1, cancellationToken);
                            completedDepth = quickMultiPv[0].CompletedDepth;
                            nodesEvaluated = ctx.QuickForcedPlayer.NodesEvaluated;
                            evaluatedNode = new BookNode(
                                state.ZobristHash,
                                level,
                                quickMultiPv[0].Score,
                                [new BookMoveEntry(quickMultiPv[0].Move.Notation, quickMultiPv[0].Score)]);
                        }
                        else
                        {
                            var multiPv = ctx.Player.SearchMultiPv(state, legalMoves, width, cancellationToken);
                            completedDepth = multiPv.Count > 0 ? multiPv[0].CompletedDepth : 0;
                            nodesEvaluated = ctx.Player.NodesEvaluated;
                            var moveEntries = multiPv
                                .Select(m => new BookMoveEntry(m.Move.Notation, m.Score))
                                .ToList();
                            int bestScore = moveEntries.Count > 0 ? moveEntries[0].Score : 0;
                            evaluatedNode = new BookNode(state.ZobristHash, level, bestScore, moveEntries);
                        }

                        TopDownNodeProgress progressReport;
                        lock (syncLock)
                        {
                            directEvaluatedNodes[state.ZobristHash] = evaluatedNode;
                            if (checkpointNodes != null)
                            {
                                checkpointNodes[state.ZobristHash] = evaluatedNode;
                            }

                            completedInLevel++;
                            totalEvaluatedOverall++;
                            progressReport = new TopDownNodeProgress(
                                evaluatedNode,
                                level,
                                completedInLevel,
                                totalInLevel,
                                totalEvaluatedOverall,
                                completedDepth,
                                nodesEvaluated,
                                FromCheckpoint: false);
                            onNodeEvaluated?.Invoke(progressReport);
                        }

                        return ctx;
                    },
                    _ => { });
            }

            var levelPositions = new List<BookDagPosition>(currentStates.Count);
            var nextStates = new List<BoardState>();
            var incomingFrontierScores = new Dictionary<ulong, List<int>>();

            foreach (var state in currentStates)
            {
                var evaluatedNode = directEvaluatedNodes[state.ZobristHash];
                var legalMoves = ruleEngine.GetLegalMoves(state);
                if (legalMoves.Count == 0 || evaluatedNode.Moves.Count == 0)
                {
                    var terminalDagNode = new BookDagPosition(state.ZobristHash, level, state, []);
                    levelPositions.Add(terminalDagNode);
                    allPositions[terminalDagNode.ZobristHash] = terminalDagNode;
                    continue;
                }

                var edges = new List<BookEdge>(Math.Min(width, evaluatedNode.Moves.Count));
                var seenEdgeTargets = new HashSet<ulong>();

                foreach (var moveEntry in evaluatedNode.Moves.Take(width))
                {
                    var matchingLegal = legalMoves.FirstOrDefault(
                        m => string.Equals(m.Notation, moveEntry.Notation, StringComparison.OrdinalIgnoreCase));
                    if (matchingLegal == null)
                    {
                        continue;
                    }

                    var childState = ruleEngine.ApplyMove(state, matchingLegal);
                    ulong childHash = childState.ZobristHash;

                    if (seenEdgeTargets.Add(childHash))
                    {
                        edges.Add(new BookEdge(matchingLegal, childHash));
                    }

                    int impliedChildScore = Math.Clamp(-moveEntry.Score, BookFile.MinScore, BookFile.MaxScore);
                    if (!incomingFrontierScores.TryGetValue(childHash, out var scoreList))
                    {
                        scoreList = [];
                        incomingFrontierScores[childHash] = scoreList;
                    }
                    scoreList.Add(impliedChildScore);

                    if (reservedHashes.Add(childHash))
                    {
                        nextStates.Add(childState);
                    }
                }

                var dagNode = new BookDagPosition(state.ZobristHash, level, state, edges);
                levelPositions.Add(dagNode);
                allPositions[dagNode.ZobristHash] = dagNode;
            }

            levels.Add(levelPositions);

            var frontierLeaves = nextStates
                .Select(s => new BookDagPosition(s.ZobristHash, level + 1, s, []))
                .ToList();
            var snapshotLevels = new List<IReadOnlyList<BookDagPosition>>(levels) { frontierLeaves };
            var snapshotAllPositions = new Dictionary<ulong, BookDagPosition>(allPositions);
            foreach (var frontierLeaf in frontierLeaves)
            {
                snapshotAllPositions[frontierLeaf.ZobristHash] = frontierLeaf;
            }

            var snapshotDag = new BookDag(variant, level + 1, width, snapshotLevels, snapshotAllPositions);
            var leafScores = new Dictionary<ulong, int>(snapshotDag.Leaves.Count);
            foreach (var leaf in snapshotDag.Leaves)
            {
                if (incomingFrontierScores.TryGetValue(leaf.ZobristHash, out var scoresList) && scoresList.Count > 0)
                {
                    leafScores[leaf.ZobristHash] = Math.Clamp(
                        (int)Math.Round(scoresList.Average()),
                        BookFile.MinScore,
                        BookFile.MaxScore);
                }
                else if (directEvaluatedNodes.TryGetValue(leaf.ZobristHash, out var termNode))
                {
                    leafScores[leaf.ZobristHash] = termNode.Score;
                }
                else
                {
                    leafScores[leaf.ZobristHash] = -BookFile.MaxScore;
                }
            }

            latestBackedUpBook = BackUp(snapshotDag, leafScores);
            onLevelComplete?.Invoke(level, latestBackedUpBook);
            currentStates = nextStates;
        }

        return latestBackedUpBook;
    }

    private sealed class TopDownWorkerContext
    {
        public RuleEngine RuleEngine { get; }
        public TranspositionTable TranspositionTable { get; }
        public MinimaxPlayer Player { get; }
        public MinimaxPlayer QuickForcedPlayer { get; }

        public TopDownWorkerContext(CheckersVariant variant, SearchLimits limits)
        {
            RuleEngine = new RuleEngine(variant);
            var evaluator = new EvaluationFunction(variant);
            TranspositionTable = new TranspositionTable(megabytes: 32);
            Player = new MinimaxPlayer(
                limits,
                ruleEngine: RuleEngine,
                evaluator: evaluator,
                transpositionTable: TranspositionTable,
                useTranspositionTable: true,
                useQuiescence: true,
                variant: variant);
            QuickForcedPlayer = new MinimaxPlayer(
                SearchLimits.FixedDepth(10, useOpeningBook: false),
                ruleEngine: RuleEngine,
                evaluator: evaluator,
                transpositionTable: TranspositionTable,
                useTranspositionTable: true,
                useQuiescence: true,
                variant: variant);
        }
    }

    /// <summary>
    /// Expands an opening book using a two-phase algorithm:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <b>Phase A — Full-Width Early Plies (<c>lv 0 .. fullWidthPlies - 1</c>):</b>
    ///       Ensures every reachable position in the first <paramref name="fullWidthPlies"/> levels stores all legal moves,
    ///       preserving any existing deeper subtrees from <paramref name="existingNodes"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Phase B — Drop-Out Expansion (DOE):</b>
    ///       Repeatedly descends from the root, filtering out candidate moves that trail a node's best move by more than
    ///       <see cref="GetDefaultDropOutDelta"/> (<paramref name="fixedDeltaCp"/>), selecting the least-visited active branch
    ///       (using virtual visits across parallel workers), expanding the chosen leaf with a Single-Search Multi-PV
    ///       (<paramref name="width"/>) search, and backing up Negamax scores bottom-up to the root.
    ///     </description>
    ///   </item>
    /// </list>
    /// </summary>
    public static List<BookNode> ExpandDropOut(
        CheckersVariant variant,
        IReadOnlyList<BookNode>? existingNodes = null,
        int fullWidthPlies = DefaultFullWidthPlies,
        int maxPly = DefaultMaxPly,
        int width = DefaultWidth,
        int? fixedDeltaCp = null,
        int? maxIterations = null,
        double? maxTimeMinutes = null,
        SearchLimits? nodeSearchLimits = null,
        int workers = DefaultWorkers,
        IDictionary<ulong, BookNode>? checkpointNodes = null,
        Action<DoeExpansionProgress>? onNodeExpanded = null,
        Action<string, IReadOnlyList<BookNode>>? onFlushSnapshot = null,
        int snapshotFlushInterval = 25,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fullWidthPlies);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fullWidthPlies, MaxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPly, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPly, MaxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxWidth);

        var limits = nodeSearchLimits ?? SearchLimits.TimePerMove(TimeSpan.FromSeconds(60), useOpeningBook: false);
        var ruleEngine = new RuleEngine(variant);
        int maxDegree = Math.Max(1, workers);
        object syncLock = new();
        var overallClock = Stopwatch.StartNew();

        var (root, nodesByHash) = BuildMutableDag(variant, ruleEngine, existingNodes, checkpointNodes, fullWidthPlies);
        BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);

        int totalSearchesReserved = 0;

        bool IsBudgetExhausted() =>
            (maxIterations.HasValue && totalSearchesReserved >= maxIterations.Value)
            || (maxTimeMinutes.HasValue && overallClock.Elapsed.TotalMinutes >= maxTimeMinutes.Value);

        // =========================================================================
        // Phase A: Full-Width Early Levels (lv 0 .. fullWidthPlies - 1)
        // =========================================================================
        int effectiveFullWidthPlies = Math.Min(fullWidthPlies, maxPly);
        for (int level = 0; level < effectiveFullWidthPlies; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsBudgetExhausted())
            {
                break;
            }

            BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);

            var levelNodes = nodesByHash.Values
                .Where(n => n.IsReachable && n.Ply == level)
                .OrderBy(n => n.ZobristHash)
                .ToList();

            var nodesToSearch = new List<MutableBookNode>();
            int totalInLevel = levelNodes.Count;
            int completedInLevel = 0;

            foreach (var node in levelNodes)
            {
                var legalMoves = ruleEngine.GetLegalMoves(node.State);
                if (legalMoves.Count == 0)
                {
                    node.IsTerminal = true;
                    node.IsExpanded = true;
                    node.Score = -BookFile.MaxScore;
                    completedInLevel++;
                    continue;
                }

                int expectedDistinctChildren = CountDistinctChildHashes(node.State, legalMoves, ruleEngine);
                if (node.IsExpanded && node.Edges.Count >= expectedDistinctChildren)
                {
                    completedInLevel++;
                    onNodeExpanded?.Invoke(new DoeExpansionProgress(
                        ToBookNode(node),
                        Phase: $"FullWidth lv {level}",
                        CompletedInPhase: completedInLevel,
                        TotalInPhase: totalInLevel,
                        TotalInternalNodes: CountReachableInternalNodes(nodesByHash),
                        TotalBookNodes: CountReachableNodes(nodesByHash),
                        ActiveFrontierLeaves: CountActiveFrontierLeaves(root, nodesByHash, maxPly, fixedDeltaCp),
                        CompletedDepth: 0,
                        NodesEvaluated: 0,
                        FromCheckpoint: true));
                }
                else
                {
                    nodesToSearch.Add(node);
                }
            }

            if (nodesToSearch.Count == 0)
            {
                continue;
            }

            Parallel.ForEach(
                nodesToSearch,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(maxDegree, nodesToSearch.Count),
                    CancellationToken = cancellationToken
                },
                () => new TopDownWorkerContext(variant, limits),
                (node, loopState, ctx) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (syncLock)
                    {
                        if (IsBudgetExhausted())
                        {
                            loopState.Stop();
                            return ctx;
                        }
                        totalSearchesReserved++;
                    }

                    var legalMoves = ctx.RuleEngine.GetLegalMoves(node.State);
                    IReadOnlyList<MultiPvMoveResult> multiPv;
                    int completedDepth;
                    long nodesEvaluated;

                    if (legalMoves.Count == 1 && level < maxPly - 1 && limits.Mode != TimeControlMode.FixedDepth)
                    {
                        multiPv = ctx.QuickForcedPlayer.SearchMultiPv(node.State, legalMoves, 1, cancellationToken);
                        completedDepth = multiPv[0].CompletedDepth;
                        nodesEvaluated = ctx.QuickForcedPlayer.NodesEvaluated;
                    }
                    else
                    {
                        multiPv = ctx.Player.SearchMultiPv(node.State, legalMoves, legalMoves.Count, cancellationToken);
                        completedDepth = multiPv.Count > 0 ? multiPv[0].CompletedDepth : 0;
                        nodesEvaluated = ctx.Player.NodesEvaluated;
                    }

                    lock (syncLock)
                    {
                        var rawMovesForCheckpoint = ApplyEvaluatedMovesToNode(node, multiPv, ctx.RuleEngine, nodesByHash);
                        BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);

                        var checkpointBookNode = new BookNode(
                            node.ZobristHash,
                            node.Ply,
                            rawMovesForCheckpoint.Count > 0 ? rawMovesForCheckpoint[0].Score : node.Score,
                            rawMovesForCheckpoint);

                        if (checkpointNodes != null)
                        {
                            checkpointNodes[node.ZobristHash] = checkpointBookNode;
                        }

                        completedInLevel++;

                        onNodeExpanded?.Invoke(new DoeExpansionProgress(
                            ToBookNode(node),
                            Phase: $"FullWidth lv {level}",
                            CompletedInPhase: completedInLevel,
                            TotalInPhase: totalInLevel,
                            TotalInternalNodes: CountReachableInternalNodes(nodesByHash),
                            TotalBookNodes: CountReachableNodes(nodesByHash),
                            ActiveFrontierLeaves: CountActiveFrontierLeaves(root, nodesByHash, maxPly, fixedDeltaCp),
                            CompletedDepth: completedDepth,
                            NodesEvaluated: nodesEvaluated,
                            FromCheckpoint: false));
                    }

                    return ctx;
                },
                _ => { });

            BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);
            onFlushSnapshot?.Invoke($"FullWidth lv {level}", ExportBookNodes(root, nodesByHash));
        }

        // =========================================================================
        // Phase B: Drop-Out Expansion (DOE) from fullWidthPlies up to maxPly
        // =========================================================================
        BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);

        int doeExpansionsCompleted = 0;
        int inFlightWorkers = 0;

        if (!IsBudgetExhausted() && root.HasAvailableDoeLeaf)
        {
            int workerCount = Math.Max(1, maxDegree);
            var workerTasks = new Task[workerCount];

            for (int w = 0; w < workerCount; w++)
            {
                workerTasks[w] = Task.Run(() =>
                {
                    var ctx = new TopDownWorkerContext(variant, limits);

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        MutableBookNode? selectedLeaf = null;
                        List<MutableBookNode>? reservedPath = null;

                        lock (syncLock)
                        {
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                if (IsBudgetExhausted())
                                {
                                    Monitor.PulseAll(syncLock);
                                    return;
                                }

                                if (TryReserveNextDoeLeaf(root, nodesByHash, maxPly, fixedDeltaCp, out selectedLeaf, out reservedPath))
                                {
                                    totalSearchesReserved++;
                                    inFlightWorkers++;
                                    break;
                                }

                                if (inFlightWorkers == 0)
                                {
                                    // No available active leaves and no workers in flight -> DOE complete!
                                    Monitor.PulseAll(syncLock);
                                    return;
                                }

                                Monitor.Wait(syncLock, millisecondsTimeout: 100);
                            }
                        }

                        bool committed = false;
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var legalMoves = ctx.RuleEngine.GetLegalMoves(selectedLeaf!.State);
                            IReadOnlyList<MultiPvMoveResult> multiPv;
                            int completedDepth = 0;
                            long nodesEvaluated = 0;

                            int nodeTargetWidth = selectedLeaf.Ply < fullWidthPlies ? legalMoves.Count : width;

                            if (legalMoves.Count == 0)
                            {
                                multiPv = [];
                            }
                            else if (legalMoves.Count == 1 && selectedLeaf.Ply < maxPly - 1 && limits.Mode != TimeControlMode.FixedDepth)
                            {
                                multiPv = ctx.QuickForcedPlayer.SearchMultiPv(selectedLeaf.State, legalMoves, 1, cancellationToken);
                                completedDepth = multiPv[0].CompletedDepth;
                                nodesEvaluated = ctx.QuickForcedPlayer.NodesEvaluated;
                            }
                            else
                            {
                                multiPv = ctx.Player.SearchMultiPv(selectedLeaf.State, legalMoves, nodeTargetWidth, cancellationToken);
                                completedDepth = multiPv.Count > 0 ? multiPv[0].CompletedDepth : 0;
                                nodesEvaluated = ctx.Player.NodesEvaluated;
                            }

                            List<BookNode>? snapshotToFlush = null;
                            string? snapshotLabel = null;

                            lock (syncLock)
                            {
                                foreach (var pathNode in reservedPath!)
                                {
                                    pathNode.PendingVisits = Math.Max(0, pathNode.PendingVisits - 1);
                                }
                                selectedLeaf.InFlight = false;
                                inFlightWorkers--;
                                committed = true;

                                if (legalMoves.Count == 0)
                                {
                                    selectedLeaf.IsTerminal = true;
                                    selectedLeaf.IsExpanded = true;
                                    selectedLeaf.Score = -BookFile.MaxScore;
                                    BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);
                                }
                                else
                                {
                                    var rawMovesForCheckpoint = ApplyEvaluatedMovesToNode(selectedLeaf, multiPv, ctx.RuleEngine, nodesByHash);
                                    BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);

                                    var checkpointBookNode = new BookNode(
                                        selectedLeaf.ZobristHash,
                                        selectedLeaf.Ply,
                                        rawMovesForCheckpoint.Count > 0 ? rawMovesForCheckpoint[0].Score : selectedLeaf.Score,
                                        rawMovesForCheckpoint);

                                    if (checkpointNodes != null)
                                    {
                                        checkpointNodes[selectedLeaf.ZobristHash] = checkpointBookNode;
                                    }
                                }

                                doeExpansionsCompleted++;

                                int activeFrontier = CountActiveFrontierLeaves(root, nodesByHash, maxPly, fixedDeltaCp);
                                onNodeExpanded?.Invoke(new DoeExpansionProgress(
                                    ToBookNode(selectedLeaf),
                                    Phase: "DOE",
                                    CompletedInPhase: doeExpansionsCompleted,
                                    TotalInPhase: maxIterations,
                                    TotalInternalNodes: CountReachableInternalNodes(nodesByHash),
                                    TotalBookNodes: CountReachableNodes(nodesByHash),
                                    ActiveFrontierLeaves: activeFrontier,
                                    CompletedDepth: completedDepth,
                                    NodesEvaluated: nodesEvaluated,
                                    FromCheckpoint: false));

                                if (snapshotFlushInterval > 0 && doeExpansionsCompleted % snapshotFlushInterval == 0)
                                {
                                    snapshotToFlush = ExportBookNodes(root, nodesByHash);
                                    snapshotLabel = $"DOE #{doeExpansionsCompleted}";
                                }

                                Monitor.PulseAll(syncLock);
                            }

                            if (snapshotToFlush != null && snapshotLabel != null)
                            {
                                onFlushSnapshot?.Invoke(snapshotLabel, snapshotToFlush);
                            }
                        }
                        finally
                        {
                            if (!committed && selectedLeaf != null && reservedPath != null)
                            {
                                lock (syncLock)
                                {
                                    foreach (var pathNode in reservedPath)
                                    {
                                        pathNode.PendingVisits = Math.Max(0, pathNode.PendingVisits - 1);
                                    }
                                    selectedLeaf.InFlight = false;
                                    inFlightWorkers--;
                                    BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);
                                    Monitor.PulseAll(syncLock);
                                }
                            }
                        }
                    }
                }, cancellationToken);
            }

            Task.WaitAll(workerTasks, cancellationToken);
        }

        BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);
        var finalExported = ExportBookNodes(root, nodesByHash);
        if (doeExpansionsCompleted > 0)
        {
            onFlushSnapshot?.Invoke("DOE Complete", finalExported);
        }

        return finalExported;
    }

    private sealed class MutableBookEdge
    {
        public Move Move { get; }
        public ulong ChildHash { get; }
        public int Score { get; set; }

        public MutableBookEdge(Move move, ulong childHash, int score)
        {
            Move = move;
            ChildHash = childHash;
            Score = score;
        }
    }

    private sealed class MutableBookNode
    {
        public ulong ZobristHash { get; }
        public BoardState State { get; }
        public int Ply { get; set; }
        public int Score { get; set; }
        public bool IsTerminal { get; set; }
        public bool IsExpanded { get; set; }
        public bool IsReachable { get; set; }
        public bool InFlight { get; set; }
        public bool HasAvailableDoeLeaf { get; set; }
        public int Visits { get; set; }
        public int PendingVisits { get; set; }
        public List<MutableBookEdge> Edges { get; } = [];

        public MutableBookNode(ulong zobristHash, BoardState state, int ply, int score = 0)
        {
            ZobristHash = zobristHash;
            State = state;
            Ply = ply;
            Score = score;
        }
    }

    private static (MutableBookNode Root, Dictionary<ulong, MutableBookNode> NodesByHash) BuildMutableDag(
        CheckersVariant variant,
        RuleEngine ruleEngine,
        IReadOnlyList<BookNode>? existingNodes,
        IDictionary<ulong, BookNode>? checkpointNodes,
        int fullWidthPlies)
    {
        var seedByHash = new Dictionary<ulong, BookNode>();
        if (existingNodes != null)
        {
            foreach (var node in existingNodes)
            {
                seedByHash[node.ZobristHash] = node;
            }
        }

        if (checkpointNodes != null)
        {
            foreach (var kvp in checkpointNodes)
            {
                if (!seedByHash.TryGetValue(kvp.Key, out var existing)
                    || kvp.Value.Moves.Count >= existing.Moves.Count)
                {
                    seedByHash[kvp.Key] = kvp.Value;
                }
            }
        }

        var initial = BoardState.CreateInitial();
        int initialScore = seedByHash.TryGetValue(initial.ZobristHash, out var seedRoot) ? seedRoot.Score : 0;
        var root = new MutableBookNode(initial.ZobristHash, initial, ply: 0, score: initialScore)
        {
            IsReachable = true
        };

        var nodesByHash = new Dictionary<ulong, MutableBookNode>
        {
            [root.ZobristHash] = root
        };

        var queue = new Queue<MutableBookNode>();
        queue.Enqueue(root);
        var expandedInBfs = new HashSet<ulong>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!expandedInBfs.Add(current.ZobristHash))
            {
                continue;
            }

            var legalMoves = ruleEngine.GetLegalMoves(current.State);
            if (legalMoves.Count == 0)
            {
                current.IsTerminal = true;
                current.IsExpanded = true;
                current.Score = -BookFile.MaxScore;
                continue;
            }

            if (!seedByHash.TryGetValue(current.ZobristHash, out var seedNode) || seedNode.Moves.Count == 0)
            {
                continue;
            }

            // Merge moves from both checkpoint and existing book if one had more moves (e.g. widened node)
            var combinedMoveEntries = new List<BookMoveEntry>(seedNode.Moves);
            if (existingNodes != null)
            {
                var orig = existingNodes.FirstOrDefault(n => n.ZobristHash == current.ZobristHash);
                if (orig != null)
                {
                    foreach (var origMove in orig.Moves)
                    {
                        if (!combinedMoveEntries.Any(m => string.Equals(m.Notation, origMove.Notation, StringComparison.OrdinalIgnoreCase)))
                        {
                            combinedMoveEntries.Add(origMove);
                        }
                    }
                }
            }

            var seenChildren = new HashSet<ulong>();
            foreach (var moveEntry in combinedMoveEntries)
            {
                var matchingLegal = legalMoves.FirstOrDefault(
                    m => string.Equals(m.Notation, moveEntry.Notation, StringComparison.OrdinalIgnoreCase));
                if (matchingLegal == null)
                {
                    continue;
                }

                var childState = ruleEngine.ApplyMove(current.State, matchingLegal);
                ulong childHash = childState.ZobristHash;

                if (childHash == current.ZobristHash || !seenChildren.Add(childHash))
                {
                    continue;
                }

                if (nodesByHash.ContainsKey(childHash) && CanReach(childHash, current.ZobristHash, nodesByHash))
                {
                    continue;
                }

                if (!nodesByHash.TryGetValue(childHash, out var childNode))
                {
                    int impliedScore = seedByHash.TryGetValue(childHash, out var childSeed) && childSeed.Moves.Count == 0
                        ? childSeed.Score
                        : Math.Clamp(-moveEntry.Score, BookFile.MinScore, BookFile.MaxScore);

                    childNode = new MutableBookNode(childHash, childState, current.Ply + 1, impliedScore)
                    {
                        IsReachable = true
                    };
                    nodesByHash[childHash] = childNode;
                }

                current.Edges.Add(new MutableBookEdge(matchingLegal, childHash, moveEntry.Score));
                queue.Enqueue(childNode);
            }

            current.IsExpanded = current.Edges.Count > 0;
        }

        return (root, nodesByHash);
    }

    private static List<BookMoveEntry> ApplyEvaluatedMovesToNode(
        MutableBookNode node,
        IReadOnlyList<MultiPvMoveResult> multiPv,
        RuleEngine ruleEngine,
        Dictionary<ulong, MutableBookNode> nodesByHash)
    {
        var existingEdgesByChild = node.Edges.ToDictionary(e => e.ChildHash);
        var seenChildren = new HashSet<ulong>();
        var updatedEdges = new List<MutableBookEdge>(multiPv.Count);
        var rawCheckpointMoves = new List<BookMoveEntry>(multiPv.Count);

        foreach (var item in multiPv)
        {
            var childState = ruleEngine.ApplyMove(node.State, item.Move);
            ulong childHash = childState.ZobristHash;

            if (childHash == node.ZobristHash || !seenChildren.Add(childHash))
            {
                continue;
            }

            if (nodesByHash.ContainsKey(childHash) && CanReach(childHash, node.ZobristHash, nodesByHash))
            {
                continue;
            }

            int impliedChildScore = Math.Clamp(-item.Score, BookFile.MinScore, BookFile.MaxScore);

            if (!nodesByHash.TryGetValue(childHash, out var childNode))
            {
                childNode = new MutableBookNode(childHash, childState, node.Ply + 1, impliedChildScore)
                {
                    IsReachable = true
                };
                nodesByHash[childHash] = childNode;
            }
            else if (!childNode.IsExpanded)
            {
                childNode.Score = impliedChildScore;
            }

            int edgeScore = childNode.IsExpanded
                ? Math.Clamp(-childNode.Score, BookFile.MinScore, BookFile.MaxScore)
                : item.Score;

            updatedEdges.Add(new MutableBookEdge(item.Move, childHash, edgeScore));
            rawCheckpointMoves.Add(new BookMoveEntry(item.Move.Notation, item.Score));
        }

        // Preserve any previously existing edges whose child was already expanded in the seed book
        foreach (var prevEdge in existingEdgesByChild.Values)
        {
            if (seenChildren.Add(prevEdge.ChildHash))
            {
                updatedEdges.Add(prevEdge);
                rawCheckpointMoves.Add(new BookMoveEntry(prevEdge.Move.Notation, prevEdge.Score));
            }
        }

        node.Edges.Clear();
        node.Edges.AddRange(updatedEdges);
        node.IsExpanded = node.Edges.Count > 0;
        return rawCheckpointMoves;
    }

    private static void BackUpMutableDag(
        MutableBookNode root,
        Dictionary<ulong, MutableBookNode> nodesByHash,
        int maxPly,
        int? fixedDeltaCp)
    {
        foreach (var node in nodesByHash.Values)
        {
            node.IsReachable = false;
        }

        // 1. Assign shortest-path Ply from root via BFS
        root.IsReachable = true;
        root.Ply = 0;
        var bfsQueue = new Queue<MutableBookNode>();
        bfsQueue.Enqueue(root);

        while (bfsQueue.Count > 0)
        {
            var u = bfsQueue.Dequeue();
            foreach (var edge in u.Edges)
            {
                if (!nodesByHash.TryGetValue(edge.ChildHash, out var v))
                {
                    continue;
                }

                if (!v.IsReachable)
                {
                    v.IsReachable = true;
                    v.Ply = u.Ply + 1;
                    bfsQueue.Enqueue(v);
                }
                else if (u.Ply + 1 < v.Ply)
                {
                    v.Ply = u.Ply + 1;
                }
            }
        }

        // 2. Topological post-order traversal from root (children always before parents)
        var postOrder = new List<MutableBookNode>(nodesByHash.Count);
        var visited = new HashSet<ulong>();

        void DfsPostOrder(MutableBookNode u)
        {
            if (!visited.Add(u.ZobristHash))
            {
                return;
            }

            foreach (var edge in u.Edges)
            {
                if (nodesByHash.TryGetValue(edge.ChildHash, out var child))
                {
                    DfsPostOrder(child);
                }
            }

            postOrder.Add(u);
        }

        DfsPostOrder(root);

        // 3. Bottom-up Negamax score back-up, Visits count, and active DOE leaf availability
        foreach (var u in postOrder)
        {
            if (!u.IsExpanded || u.Edges.Count == 0)
            {
                if (u.IsTerminal)
                {
                    u.Score = -BookFile.MaxScore;
                    u.Visits = 1;
                    u.HasAvailableDoeLeaf = false;
                }
                else
                {
                    u.Visits = 0;
                    u.HasAvailableDoeLeaf = !u.InFlight && u.Ply < maxPly;
                }
                continue;
            }

            foreach (var edge in u.Edges)
            {
                var child = nodesByHash[edge.ChildHash];
                edge.Score = Math.Clamp(-child.Score, BookFile.MinScore, BookFile.MaxScore);
            }

            u.Edges.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Move.Notation, b.Move.Notation);
            });

            u.Score = u.Edges[0].Score;
            int bestScore = u.Score;
            int delta = GetDefaultDropOutDelta(u.Ply, fixedDeltaCp);

            int visitsSum = 1;
            bool anyActiveLeaf = false;

            foreach (var edge in u.Edges)
            {
                var child = nodesByHash[edge.ChildHash];
                visitsSum += child.Visits;

                bool isActiveMove = (bestScore - edge.Score) <= delta;
                if (isActiveMove && child.HasAvailableDoeLeaf)
                {
                    anyActiveLeaf = true;
                }
            }

            u.Visits = visitsSum;
            u.HasAvailableDoeLeaf = anyActiveLeaf;
        }
    }

    private static bool TryReserveNextDoeLeaf(
        MutableBookNode root,
        Dictionary<ulong, MutableBookNode> nodesByHash,
        int maxPly,
        int? fixedDeltaCp,
        out MutableBookNode? selectedLeaf,
        out List<MutableBookNode>? reservedPath)
    {
        selectedLeaf = null;
        reservedPath = null;

        if (!root.HasAvailableDoeLeaf)
        {
            return false;
        }

        var path = new List<MutableBookNode> { root };
        var current = root;
        var visitedInDescent = new HashSet<ulong> { root.ZobristHash };

        while (current.IsExpanded && current.Edges.Count > 0)
        {
            int bestScore = current.Score;
            int delta = GetDefaultDropOutDelta(current.Ply, fixedDeltaCp);

            MutableBookNode? bestChild = null;
            MutableBookEdge? bestEdge = null;
            int minEffectiveVisits = int.MaxValue;

            foreach (var edge in current.Edges)
            {
                if ((bestScore - edge.Score) > delta)
                {
                    continue;
                }

                if (!nodesByHash.TryGetValue(edge.ChildHash, out var child)
                    || !child.HasAvailableDoeLeaf
                    || visitedInDescent.Contains(child.ZobristHash))
                {
                    continue;
                }

                int effectiveVisits = child.Visits + child.PendingVisits;
                if (bestChild == null
                    || effectiveVisits < minEffectiveVisits
                    || (effectiveVisits == minEffectiveVisits && edge.Score > bestEdge!.Score)
                    || (effectiveVisits == minEffectiveVisits && edge.Score == bestEdge!.Score && child.Ply < bestChild.Ply))
                {
                    bestChild = child;
                    bestEdge = edge;
                    minEffectiveVisits = effectiveVisits;
                }
            }

            if (bestChild == null)
            {
                return false;
            }

            visitedInDescent.Add(bestChild.ZobristHash);
            path.Add(bestChild);
            current = bestChild;
        }

        if (current.IsExpanded || current.IsTerminal || current.InFlight || current.Ply >= maxPly)
        {
            return false;
        }

        current.InFlight = true;
        foreach (var node in path)
        {
            node.PendingVisits++;
        }

        BackUpMutableDag(root, nodesByHash, maxPly, fixedDeltaCp);
        selectedLeaf = current;
        reservedPath = path;
        return true;
    }

    private static int CountDistinctChildHashes(BoardState state, IReadOnlyList<Move> legalMoves, RuleEngine ruleEngine)
    {
        var hashes = new HashSet<ulong>();
        foreach (var move in legalMoves)
        {
            hashes.Add(ruleEngine.ApplyMove(state, move).ZobristHash);
        }
        return hashes.Count;
    }

    private static int CountReachableNodes(Dictionary<ulong, MutableBookNode> nodesByHash) =>
        nodesByHash.Values.Count(n => n.IsReachable);

    private static int CountReachableInternalNodes(Dictionary<ulong, MutableBookNode> nodesByHash) =>
        nodesByHash.Values.Count(n => n.IsReachable && n.IsExpanded && n.Edges.Count > 0);

    private static int CountActiveFrontierLeaves(
        MutableBookNode root,
        Dictionary<ulong, MutableBookNode> nodesByHash,
        int maxPly,
        int? fixedDeltaCp)
    {
        var activeVisited = new HashSet<ulong>();
        var activeLeaves = new HashSet<ulong>();
        var queue = new Queue<MutableBookNode>();

        queue.Enqueue(root);
        activeVisited.Add(root.ZobristHash);

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            if (!u.IsExpanded || u.Edges.Count == 0)
            {
                if (!u.IsTerminal && u.Ply < maxPly)
                {
                    activeLeaves.Add(u.ZobristHash);
                }
                continue;
            }

            int bestScore = u.Score;
            int delta = GetDefaultDropOutDelta(u.Ply, fixedDeltaCp);

            foreach (var edge in u.Edges)
            {
                if ((bestScore - edge.Score) <= delta
                    && nodesByHash.TryGetValue(edge.ChildHash, out var child)
                    && activeVisited.Add(child.ZobristHash))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return activeLeaves.Count;
    }

    private static bool CanReach(ulong startHash, ulong targetHash, Dictionary<ulong, MutableBookNode> nodesByHash)
    {
        if (startHash == targetHash)
        {
            return true;
        }

        if (!nodesByHash.TryGetValue(startHash, out var startNode))
        {
            return false;
        }

        var visited = new HashSet<ulong> { startHash };
        var queue = new Queue<MutableBookNode>();
        queue.Enqueue(startNode);

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            foreach (var edge in curr.Edges)
            {
                if (edge.ChildHash == targetHash)
                {
                    return true;
                }

                if (nodesByHash.TryGetValue(edge.ChildHash, out var next) && visited.Add(next.ZobristHash))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return false;
    }

    private static BookNode ToBookNode(MutableBookNode node)
    {
        var moves = node.Edges
            .Select(e => new BookMoveEntry(e.Move.Notation, e.Score))
            .ToList();
        return new BookNode(node.ZobristHash, node.Ply, node.Score, moves);
    }

    private static List<BookNode> ExportBookNodes(MutableBookNode root, Dictionary<ulong, MutableBookNode> nodesByHash)
    {
        return nodesByHash.Values
            .Where(n => n.IsReachable)
            .OrderBy(n => n.Ply)
            .ThenBy(n => n.ZobristHash)
            .Select(ToBookNode)
            .ToList();
    }

    /// <summary>
    /// Builds a complete opening book in one call by enumerating the DAG, evaluating all unique leaves
    /// with <paramref name="evaluateLeaf"/>, and backing up scores to the root.
    /// </summary>
    public static List<BookNode> Build(
        CheckersVariant variant,
        int depth,
        int width,
        Func<BoardState, IReadOnlyList<Move>, IReadOnlyList<Move>> selectTopMoves,
        Func<BoardState, int> evaluateLeaf)
    {
        BookDag dag = Enumerate(variant, depth, width, selectTopMoves);
        var leafScores = dag.Leaves.ToDictionary(
            leaf => leaf.ZobristHash,
            leaf => evaluateLeaf(leaf.State));
        return BackUp(dag, leafScores);
    }

    /// <summary>
    /// Verifies that every internal node in <paramref name="nodes"/> has:
    /// (1) its moves sorted descending by score,
    /// (2) its node score equal to its highest move score, and
    /// (3) for every reachable position from the initial board, each stored move's score equal to <c>-child.Score</c>.
    /// Returns the list of inconsistent nodes (empty if the book is 100% consistent).
    /// </summary>
    public static List<BookNode> CheckBackUp(IReadOnlyList<BookNode> nodes, CheckersVariant variant)
    {
        var byHash = nodes.ToDictionary(n => n.ZobristHash);
        var inconsistent = new HashSet<ulong>();
        var result = new List<BookNode>();

        foreach (var node in nodes)
        {
            if (node.Moves.Count == 0)
            {
                continue;
            }

            int maxMoveScore = node.Moves.Max(m => m.Score);
            bool sorted = true;
            for (int i = 1; i < node.Moves.Count; i++)
            {
                if (node.Moves[i].Score > node.Moves[i - 1].Score)
                {
                    sorted = false;
                    break;
                }
            }

            if (node.Score != maxMoveScore || !sorted)
            {
                if (inconsistent.Add(node.ZobristHash))
                {
                    result.Add(node);
                }
            }
        }

        // Traverse reachable states from the initial board along stored book moves to verify parent/child Negamax consistency
        var ruleEngine = new RuleEngine(variant);
        var initial = BoardState.CreateInitial();
        if (byHash.ContainsKey(initial.ZobristHash))
        {
            var visited = new HashSet<ulong> { initial.ZobristHash };
            var queue = new Queue<BoardState>();
            queue.Enqueue(initial);

            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                if (!byHash.TryGetValue(state.ZobristHash, out var node) || node.Moves.Count == 0)
                {
                    continue;
                }

                var legalMoves = ruleEngine.GetLegalMoves(state);
                foreach (var moveEntry in node.Moves)
                {
                    var matchingLegal = legalMoves.FirstOrDefault(
                        m => string.Equals(m.Notation, moveEntry.Notation, StringComparison.OrdinalIgnoreCase));

                    if (matchingLegal == null)
                    {
                        if (inconsistent.Add(node.ZobristHash))
                        {
                            result.Add(node);
                        }
                        continue;
                    }

                    var childState = ruleEngine.ApplyMove(state, matchingLegal);
                    if (!byHash.TryGetValue(childState.ZobristHash, out var childNode)
                        || moveEntry.Score != -childNode.Score)
                    {
                        if (inconsistent.Add(node.ZobristHash))
                        {
                            result.Add(node);
                        }
                        continue;
                    }

                    if (visited.Add(childState.ZobristHash))
                    {
                        queue.Enqueue(childState);
                    }
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<Move> RankAndSelectMoves(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        int width,
        RuleEngine ruleEngine,
        EvaluationFunction evaluator,
        MinimaxPlayer player,
        CancellationToken cancellationToken)
    {
        var scoredMoves = new List<(Move Move, int Score)>(legalMoves.Count);

        foreach (var move in legalMoves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childState = ruleEngine.ApplyMove(state, move);
            int childScore = EvaluateLeafPosition(childState, ruleEngine, evaluator, player, cancellationToken);
            int moveScore = Math.Clamp(-childScore, BookFile.MinScore, BookFile.MaxScore);
            scoredMoves.Add((move, moveScore));
        }

        return scoredMoves
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Move.Notation, StringComparer.Ordinal)
            .Take(width)
            .Select(x => x.Move)
            .ToList();
    }

    internal static int ParseAnalysisScore(string? valueText, BoardState state, IEvaluationFunction evaluator)
    {
        if (string.IsNullOrWhiteSpace(valueText))
        {
            var pos = state.BitPosition;
            return evaluator.Evaluate(in pos);
        }

        if (valueText.Contains("Win", StringComparison.OrdinalIgnoreCase))
        {
            return valueText.StartsWith('-') ? -29_000 : 29_000;
        }

        if (valueText.Contains("Loss", StringComparison.OrdinalIgnoreCase))
        {
            return -29_000;
        }

        string clean = valueText.Replace("+", "", StringComparison.Ordinal).Trim();
        if (int.TryParse(clean, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        var fallbackPos = state.BitPosition;
        return evaluator.Evaluate(in fallbackPos);
    }
}
