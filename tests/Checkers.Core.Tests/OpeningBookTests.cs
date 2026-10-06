using Checkers.Core.AI;
using Checkers.Core.AI.Book;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class OpeningBookTests
{
    [Fact]
    public void BookFile_RoundTrips_NodesAndPartialLeaves()
    {
        var nodes = new List<BookNode>
        {
            new(0x9A4F82C10B3E7712UL, 0, 8, [
                new BookMoveEntry("22-18", 8),
                new BookMoveEntry("23-18", 4),
                new BookMoveEntry("24-19", 0)
            ]),
            new(0x3C81E042A915B604UL, 1, -8, [
                new BookMoveEntry("11-15", -8),
                new BookMoveEntry("9-14", -12)
            ]),
            new(0x1122334455667788UL, 2, 12, [])
        };

        using var writer = new StringWriter();
        BookFile.Write(writer, nodes, "Test Checkers Opening Book\nDepth 2, Width 3");
        string text = writer.ToString();

        text.Should().Contain("# Test Checkers Opening Book");
        text.Should().Contain("9A4F82C10B3E7712 0 +8 22-18:+8 23-18:+4 24-19:0");
        text.Should().Contain("3C81E042A915B604 1 -8 11-15:-8 9-14:-12");
        text.Should().Contain("1122334455667788 2 +12");

        using var reader = new StringReader(text);
        var parsed = BookFile.Read(reader);

        parsed.Should().HaveCount(3);
        parsed[0].ZobristHash.Should().Be(0x9A4F82C10B3E7712UL);
        parsed[0].Ply.Should().Be(0);
        parsed[0].Score.Should().Be(8);
        parsed[0].Moves.Should().Equal(
            new BookMoveEntry("22-18", 8),
            new BookMoveEntry("23-18", 4),
            new BookMoveEntry("24-19", 0));

        parsed[2].Moves.Should().BeEmpty();
        parsed[2].Score.Should().Be(12);

        // Partial leaf format round-trip
        string partialLine = BookFile.FormatPartialLeaf(0xCAFEBABE12345678UL, -14);
        BookFile.TryParsePartialLeaf(partialLine, out ulong pHash, out int pScore).Should().BeTrue();
        pHash.Should().Be(0xCAFEBABE12345678UL);
        pScore.Should().Be(-14);
    }

    [Theory]
    [InlineData(CheckersVariant.English)]
    [InlineData(CheckersVariant.International)]
    public void BookBuilder_Enumerate_MergesTranspositionsAndProducesConsistentBackUp(CheckersVariant variant)
    {
        // Deterministic selector: pick first 3 moves ordered by notation
        var dag = BookBuilder.Enumerate(
            variant: variant,
            depth: 4,
            width: 3,
            selectTopMoves: (_, moves) => moves.OrderBy(m => m.Notation, StringComparer.Ordinal).Take(3).ToList());

        dag.Depth.Should().Be(4);
        dag.Width.Should().Be(3);
        dag.Levels[0].Should().ContainSingle();
        dag.Levels[1].Should().HaveCount(3);
        dag.Levels[2].Should().HaveCount(9);

        // At ply 3 and ply 4, White's and Black's commuting opening moves produce transpositions,
        // so the unique position count at ply 4 is strictly less than raw 3^4 = 81!
        dag.Levels[3].Count.Should().BeLessThan(27);
        dag.Levels[4].Count.Should().BeLessThan(81);

        var eval = new EvaluationFunction(variant);
        var leafScores = dag.Leaves.ToDictionary(
            leaf => leaf.ZobristHash,
            leaf => eval.Evaluate(leaf.State));

        var nodes = BookBuilder.BackUp(dag, leafScores);
        nodes.Should().HaveCount(dag.TotalPositions);

        var inconsistent = BookBuilder.CheckBackUp(nodes, variant);
        inconsistent.Should().BeEmpty();
    }

    [Fact]
    public void OpeningBook_MatchesTransposedPositionReachedByDifferentMoveOrder()
    {
        var engine = new RuleEngine(CheckersVariant.International);
        var eval = new EvaluationFunction(CheckersVariant.International);

        string[] preferred = ["21-17", "23-19", "24-20", "11-16", "12-16", "9-13"];

        // Build a 3-ply, width-3 book prioritizing the quiet commuting moves
        var nodes = BookBuilder.Build(
            CheckersVariant.International,
            depth: 3,
            width: 3,
            selectTopMoves: (_, moves) => moves
                .OrderBy(m => Array.IndexOf(preferred, m.Notation) is >= 0 and int idx ? idx : 999)
                .ThenBy(m => m.Notation, StringComparer.Ordinal)
                .Take(3)
                .ToList(),
            evaluateLeaf: s => eval.Evaluate(s));

        var book = OpeningBook.FromNodes(nodes);

        // Sequence A: White plays 21-17, Black plays 11-16, White plays 23-19
        var sA = BoardState.CreateInitial();
        sA = engine.ApplyMove(sA, engine.GetLegalMoves(sA).First(m => m.Notation == "21-17"));
        sA = engine.ApplyMove(sA, engine.GetLegalMoves(sA).First(m => m.Notation == "11-16"));
        sA = engine.ApplyMove(sA, engine.GetLegalMoves(sA).First(m => m.Notation == "23-19"));

        // Sequence B (transposed move order): White plays 23-19, Black plays 11-16, White plays 21-17
        var sB = BoardState.CreateInitial();
        sB = engine.ApplyMove(sB, engine.GetLegalMoves(sB).First(m => m.Notation == "23-19"));
        sB = engine.ApplyMove(sB, engine.GetLegalMoves(sB).First(m => m.Notation == "11-16"));
        sB = engine.ApplyMove(sB, engine.GetLegalMoves(sB).First(m => m.Notation == "21-17"));

        sA.ZobristHash.Should().Be(sB.ZobristHash);
        book.TryGetScore(sA, out int scoreA).Should().BeTrue();
        book.TryGetScore(sB, out int scoreB).Should().BeTrue();
        scoreA.Should().Be(scoreB);
    }

    [Fact]
    public void OpeningBook_TryGetMove_SelectsRandomlyWithinMarginAndExcludesInferiorMoves()
    {
        var engine = new RuleEngine(CheckersVariant.English);
        var initial = BoardState.CreateInitial();
        var legalMoves = engine.GetLegalMoves(initial);

        // Store 3 book moves at the initial position:
        // "11-15" (+10 cp), "9-14" (+4 cp -> within 10 cp margin), "10-15" (-5 cp -> 15 cp worse, outside 10 cp margin)
        // Note: initial board has White to move (21-17, 22-18, 23-18, etc.)
        var node = new BookNode(
            initial.ZobristHash,
            Ply: 0,
            Score: 10,
            Moves:
            [
                new BookMoveEntry("22-18", 10),
                new BookMoveEntry("23-18", 4),
                new BookMoveEntry("21-17", -5)
            ]);

        var book = OpeningBook.FromNodes([node]);
        var rng = new Random(42);
        var pickedNotations = new HashSet<string>();

        for (int i = 0; i < 50; i++)
        {
            book.TryGetMove(initial, legalMoves, rng, marginCp: 10, out var move, out int score).Should().BeTrue();
            pickedNotations.Add(move!.Notation);
            score.Should().BeOneOf(10, 4);
        }

        // Both moves within 10 cp ("22-18" at +10 and "23-18" at +4) should be picked, while "21-17" (-5) is never picked
        pickedNotations.Should().BeEquivalentTo(["22-18", "23-18"]);
    }

    [Fact]
    public async Task MinimaxPlayer_UsesOpeningBookWhenEnabled_AndSearchesWhenOutOfBook()
    {
        var engine = new RuleEngine(CheckersVariant.International);
        var eval = new EvaluationFunction(CheckersVariant.International);

        var dag = BookBuilder.EnumerateWithSearch(
            variant: CheckersVariant.International,
            depth: 2,
            width: 3,
            selectDepth: 4,
            workers: 2);

        var leafScores = dag.Leaves.ToDictionary(
            l => l.ZobristHash,
            l => eval.Evaluate(l.State));

        var book = OpeningBook.FromNodes(BookBuilder.BackUp(dag, leafScores));
        var initial = BoardState.CreateInitial();
        var legalMoves = engine.GetLegalMoves(initial);

        var player = new MinimaxPlayer(
            SearchLimits.FixedDepth(4, useOpeningBook: true),
            ruleEngine: engine,
            evaluator: eval,
            openingBook: book);

        var move = await player.GetMoveAsync(initial, legalMoves);

        player.NodesEvaluated.Should().Be(0);
        player.LastAnalysis.Should().NotBeNull();
        player.LastAnalysis!.FromBook.Should().BeTrue();
        player.LastAnalysis.Depth.Should().Be("Book (2 plies)");
        book.TryGetNode(initial.ZobristHash, out var rootNode).Should().BeTrue();
        rootNode!.Moves.Select(m => m.Notation).Should().Contain(move.Notation);

        // When UseOpeningBook is false, MinimaxPlayer performs a normal tree search
        player.UseOpeningBook = false;
        await player.GetMoveAsync(initial, legalMoves);
        player.NodesEvaluated.Should().BeGreaterThan(0);
        player.LastAnalysis!.FromBook.Should().BeFalse();
    }

    [Fact]
    public void SearchMultiPv_ReturnsTopKDistinctSortedMoves_AtFixedDepth()
    {
        var engine = new RuleEngine(CheckersVariant.English);
        var initial = BoardState.CreateInitial();
        var legalMoves = engine.GetLegalMoves(initial);

        var player = new MinimaxPlayer(
            SearchLimits.FixedDepth(6, useOpeningBook: false),
            ruleEngine: engine,
            variant: CheckersVariant.English);

        var top3 = player.SearchMultiPv(initial, legalMoves, multiPvCount: 3);

        top3.Should().HaveCount(3);
        top3.Select(m => m.Move.Notation).Should().OnlyHaveUniqueItems();
        top3[0].Score.Should().BeGreaterThanOrEqualTo(top3[1].Score);
        top3[1].Score.Should().BeGreaterThanOrEqualTo(top3[2].Score);
        top3.All(m => m.CompletedDepth == 6).Should().BeTrue();
    }

    [Fact]
    public void GenerateTopDown_BuildsConsistentBook_AndSupportsCheckpointResume()
    {
        var checkpoint = new Dictionary<ulong, BookNode>();
        var completedLevels = new List<int>();

        // First generate lv 0..1 (2 plies of moves, reaching lv 2)
        var bookLv1 = BookBuilder.GenerateTopDown(
            variant: CheckersVariant.English,
            maxEvaluatedLevel: 1,
            width: 3,
            nodeSearchLimits: SearchLimits.FixedDepth(4, useOpeningBook: false),
            workers: 2,
            checkpointNodes: checkpoint,
            onLevelComplete: (lvl, _) => completedLevels.Add(lvl));

        completedLevels.Should().Equal([0, 1]);
        checkpoint.Should().HaveCount(4); // 1 at lv 0 + 3 at lv 1
        BookBuilder.CheckBackUp(bookLv1, CheckersVariant.English).Should().BeEmpty();

        // Now resume from checkpoint and extend to lv 2 (3 plies of moves, reaching lv 3)
        int restoredFromCheckpoint = 0;
        int newlySearched = 0;
        var bookLv2 = BookBuilder.GenerateTopDown(
            variant: CheckersVariant.English,
            maxEvaluatedLevel: 2,
            width: 3,
            nodeSearchLimits: SearchLimits.FixedDepth(4, useOpeningBook: false),
            workers: 2,
            checkpointNodes: checkpoint,
            onNodeEvaluated: p =>
            {
                if (p.FromCheckpoint)
                {
                    restoredFromCheckpoint++;
                }
                else
                {
                    newlySearched++;
                }
            });

        restoredFromCheckpoint.Should().Be(4); // lv 0 (1) and lv 1 (3) restored without re-searching
        newlySearched.Should().Be(9); // Only lv 2 (9 nodes) newly searched
        BookBuilder.CheckBackUp(bookLv2, CheckersVariant.English).Should().BeEmpty();

        var loadedBook = OpeningBook.FromNodes(bookLv2);
        loadedBook.Depth.Should().Be(3);
    }

    [Fact]
    public void ReadPartialNodes_IgnoresCommentsAndTruncatedTrailingLine()
    {
        const string partialText = """
            # Partial checkpoint header
            9A4F82C10B3E7712 0 +4 11-15:+4 9-14:+2 10-15:+1
            3C81E042A915B604 1 -4 22-18:-4 23-18:-5
            TRUNCATED_LINE_DURING_CRASH 2 +
            """;

        using var reader = new StringReader(partialText);
        var map = BookFile.ReadPartialNodes(reader);

        map.Should().HaveCount(2);
        map.ContainsKey(0x9A4F82C10B3E7712UL).Should().BeTrue();
        map.ContainsKey(0x3C81E042A915B604UL).Should().BeTrue();
    }

    [Fact]
    public void ExpandDropOut_WidensEarlyLevelsToAllLegalMoves_PreservesSubtrees_AndPassesCheckBackUp()
    {
        // 1. Generate a width-3 seed book up to lv 2 (reaching lv 3 leaves)
        var seedBook = BookBuilder.GenerateTopDown(
            variant: CheckersVariant.English,
            maxEvaluatedLevel: 2,
            width: 3,
            nodeSearchLimits: SearchLimits.FixedDepth(4, useOpeningBook: false),
            workers: 2);

        var seedLv2InternalNodes = seedBook
            .Where(n => n.Ply == 2 && n.Moves.Count > 0)
            .ToDictionary(n => n.ZobristHash);
        seedLv2InternalNodes.Should().HaveCount(9);

        var checkpoint = new Dictionary<ulong, BookNode>();

        // 2. Run ExpandDropOut with fullWidthPlies = 2 (lv 0..1 store all 7 legal moves) and DOE up to maxPly = 4
        var expandedBook = BookBuilder.ExpandDropOut(
            variant: CheckersVariant.English,
            existingNodes: seedBook,
            fullWidthPlies: 2,
            maxPly: 4,
            width: 3,
            maxIterations: 16,
            nodeSearchLimits: SearchLimits.FixedDepth(4, useOpeningBook: false),
            workers: 2,
            checkpointNodes: checkpoint);

        BookBuilder.CheckBackUp(expandedBook, CheckersVariant.English).Should().BeEmpty();

        // Root (lv 0) and all 7 lv 1 positions must now store all 7 legal moves
        var root = expandedBook.Single(n => n.Ply == 0);
        root.Moves.Should().HaveCount(7);

        var lv1Nodes = expandedBook.Where(n => n.Ply == 1).ToList();
        lv1Nodes.Should().HaveCount(7);
        lv1Nodes.Should().OnlyContain(n => n.Moves.Count == 7);

        // All 9 original lv 2 internal nodes from the seed book must still be present and preserve their moves
        var expandedByHash = expandedBook.ToDictionary(n => n.ZobristHash);
        foreach (var (origLv2Hash, origSeedNode) in seedLv2InternalNodes)
        {
            expandedByHash.Should().ContainKey(origLv2Hash);
            expandedByHash[origLv2Hash].Moves.Count.Should().BeGreaterThanOrEqualTo(origSeedNode.Moves.Count);
        }

        // 8 Phase A widenings (1 at lv 0 + 7 at lv 1) + 8 Phase B DOE expansions of newly added competitive lv 2 leaves = 17 expanded lv 2 nodes
        expandedBook.Count(n => n.Ply == 2 && n.Moves.Count > 0).Should().Be(9 + 8);
    }

    [Fact]
    public void ExpandDropOut_SkipsDroppedOutBranches_AndExpandsCompetitiveLines()
    {
        var engine = new RuleEngine(CheckersVariant.English);
        var initial = BoardState.CreateInitial();
        var legal = engine.GetLegalMoves(initial);

        var moveBest = legal.First(m => m.Notation == "22-18");
        var moveClose = legal.First(m => m.Notation == "22-17");
        var moveBad = legal.First(m => m.Notation == "21-17");

        var childBest = engine.ApplyMove(initial, moveBest);
        var childClose = engine.ApplyMove(initial, moveClose);
        var childBad = engine.ApplyMove(initial, moveBad);

        // Construct a 1-ply seed book where 22-18 (+10) and 22-17 (+6) are within delta = 5 cp,
        // while 21-17 (-40) trails by 50 cp and must drop out of DOE expansion.
        var seedNodes = new List<BookNode>
        {
            new(initial.ZobristHash, 0, 10, [
                new BookMoveEntry("22-18", 10),
                new BookMoveEntry("22-17", 6),
                new BookMoveEntry("21-17", -40)
            ]),
            new(childBest.ZobristHash, 1, -10, []),
            new(childClose.ZobristHash, 1, -6, []),
            new(childBad.ZobristHash, 1, 40, [])
        };

        var expanded = BookBuilder.ExpandDropOut(
            variant: CheckersVariant.English,
            existingNodes: seedNodes,
            fullWidthPlies: 0,
            maxPly: 3,
            width: 2,
            fixedDeltaCp: 5,
            nodeSearchLimits: SearchLimits.FixedDepth(4, useOpeningBook: false),
            workers: 2);

        BookBuilder.CheckBackUp(expanded, CheckersVariant.English).Should().BeEmpty();
        expanded.Max(n => n.Ply).Should().Be(3);

        var byHash = expanded.ToDictionary(n => n.ZobristHash);
        byHash[childBest.ZobristHash].Moves.Should().NotBeEmpty("best move child is active (0 cp behind)");
        byHash[childBad.ZobristHash].Moves.Should().BeEmpty("dropped-out move child (50 cp behind > delta 5 cp) must never be expanded");
    }
}


