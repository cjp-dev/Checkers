using System.Collections.Concurrent;
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
/// Builds and verifies transposition-aware Checkers opening books using either Top-Down Level-by-Level
/// Multi-PV search (<see cref="GenerateTopDown"/>) or breadth-first DAG enumeration followed by bottom-up
/// Negamax score back-up (<see cref="BackUp"/>).
/// </summary>
public static class BookBuilder
{
    public const int MaxDepth = 12;
    public const int MaxWidth = 7;
    public const int DefaultMaxEvaluatedLevel = 7;
    public const int DefaultDepth = 7;
    public const int DefaultWidth = 3;
    public const int DefaultSelectDepth = 12;
    public const int DefaultWorkers = 8;

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
