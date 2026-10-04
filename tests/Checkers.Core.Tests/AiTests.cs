using Checkers.Core.AI;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class AiTests
{
    private readonly RuleEngine _ruleEngine = new();
    private readonly EvaluationFunction _evaluator = new();

    [Fact]
    public void EvaluationFunction_InitialBoard_HasZeroNetScore()
    {
        var board = BoardState.CreateInitial();
        int score = _evaluator.Evaluate(board);

        // Symmetric layout
        score.Should().Be(0);
    }

    [Fact]
    public void EvaluationFunction_KingGivesMajorMaterialAdvantage()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(new Position(3, 2), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(new Position(0, 1), new Piece(PieceColor.Black, PieceType.Man));

        int score = _evaluator.Evaluate(board);
        score.Should().BeGreaterThan(150); // King (300) > Man (100)
    }

    [Fact]
    public async Task RandomPlayer_SelectsLegalMove()
    {
        var board = BoardState.CreateInitial();
        var legalMoves = _ruleEngine.GetLegalMoves(board);

        var player = new RandomPlayer();
        var move = await player.GetMoveAsync(board, legalMoves);

        legalMoves.Should().Contain(move);
    }

    [Fact]
    public async Task MinimaxPlayer_FindsImmediateWinningCapture()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        // White at (4, 3)
        var whitePos = new Position(4, 3);
        board.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));

        // Black at (3, 2)
        var blackPos = new Position(3, 2);
        board.SetPiece(blackPos, new Piece(PieceColor.Black, PieceType.Man));

        var legalMoves = _ruleEngine.GetLegalMoves(board);

        var ai = new MinimaxPlayer(depth: 3);
        var chosenMove = await ai.GetMoveAsync(board, legalMoves);

        chosenMove.IsCapture.Should().BeTrue();
        chosenMove.To.Should().Be(new Position(2, 1));
        chosenMove.CapturedPositions.Should().Contain(blackPos);
    }

    [Fact]
    public async Task MinimaxPlayer_RespectsCancellationToken()
    {
        var board = BoardState.CreateInitial();
        var legalMoves = _ruleEngine.GetLegalMoves(board);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ai = new MinimaxPlayer(depth: 4);

        var act = async () => await ai.GetMoveAsync(board, legalMoves, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly List<T> _items = [];
        public List<T> Items
        {
            get
            {
                lock (_items) return [.. _items];
            }
        }

        public void Report(T value)
        {
            lock (_items)
            {
                _items.Add(value);
            }
        }
    }

    [Fact]
    public async Task MinimaxPlayer_ReportsProgressDuringSearch()
    {
        var board = BoardState.CreateInitial();
        var legalMoves = _ruleEngine.GetLegalMoves(board);

        var ai = new MinimaxPlayer(depth: 3);
        var progress = new SyncProgress<SearchAnalysis>();

        var move = await ai.GetMoveAsync(board, legalMoves, progress, CancellationToken.None);

        legalMoves.Should().Contain(move);
        var reports = progress.Items;
        reports.Should().NotBeEmpty();
        reports.Should().Contain(r => r.Depth.Contains("1 plies"));
        reports.Last().Move.Should().NotBe("-");
        reports.Last().Nodes.Should().NotBe("-");
        reports.Last().Time.Should().NotBe("-");
    }

    [Fact]
    public async Task MinimaxPlayer_SingleLegalMove_ReportsForcedAnalysisProgress()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(new Position(5, 0), new Piece(PieceColor.White, PieceType.Man));
        // Single legal move: (5,0) -> (4,1)
        var legalMoves = _ruleEngine.GetLegalMoves(board);
        legalMoves.Should().HaveCount(1);

        var ai = new MinimaxPlayer(depth: 3);
        var progress = new SyncProgress<SearchAnalysis>();

        var move = await ai.GetMoveAsync(board, legalMoves, progress, CancellationToken.None);

        move.Should().Be(legalMoves[0]);
        var reports = progress.Items;
        reports.Should().ContainSingle();
        reports[0].Depth.Should().Be("1 ply (Forced)");
        reports[0].Value.Should().Be("Forced");
        reports[0].BestMove.Should().Be(legalMoves[0].Notation);
    }

    [Fact]
    public void DrawTable_DetectsSearchPathAndSeededHistoryRepetitions()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(new Position(4, 3), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(new Position(1, 2), new Piece(PieceColor.Black, PieceType.King));
        board.RecalculateHash();

        var rootPos = board.BitPosition;
        var drawTable = new DrawTable();

        ulong repeatedGameHash = 0xCAFEBABE12345678UL;
        drawTable.Reset(in rootPos, [repeatedGameHash, 0x1111UL, repeatedGameHash, rootPos.Hash]);

        // 1. Seeded 2-fold hash triggers immediately at ply 1
        var seededPos = rootPos;
        seededPos.SideToMove = PieceColor.Black;
        seededPos.HalfMoveClock = 1;
        seededPos.Hash = repeatedGameHash;
        drawTable.IsRepetition(in seededPos, ply: 1).Should().BeTrue();

        // 2. Search-path repetition at ply 4 (returning to rootPos with HalfMoveClock >= 4)
        drawTable.Record(1, 0xAAA1UL);
        drawTable.Record(2, 0xAAA2UL);
        drawTable.Record(3, 0xAAA3UL);

        var cyclePos = rootPos;
        cyclePos.HalfMoveClock = 4;
        drawTable.IsRepetition(in cyclePos, ply: 4).Should().BeTrue();

        // 3. If a capture reset HalfMoveClock to 1, ply 4 is NOT a repetition
        var postCapturePos = rootPos;
        postCapturePos.HalfMoveClock = 1;
        drawTable.IsRepetition(in postCapturePos, ply: 4).Should().BeFalse();
    }

    [Fact]
    public async Task MinimaxPlayer_WhenWinning_AvoidsThreefoldRepetitionDraw()
    {
        // White has 2 Kings vs 1 Black King (clearly winning ~ +300)
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(new Position(5, 2), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(new Position(6, 5), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(new Position(0, 7), new Piece(PieceColor.Black, PieceType.King));
        board.RecalculateHash();

        var legalMoves = _ruleEngine.GetLegalMoves(board);
        legalMoves.Count.Should().BeGreaterThan(1);

        var ai = new MinimaxPlayer(depth: 4, variant: CheckersVariant.English);

        // First find the move White normally prefers without repetition history
        var preferredMove = await ai.GetMoveAsync(board, legalMoves);
        var repeatedTargetState = _ruleEngine.ApplyMove(board, preferredMove);

        // Now pretend repeatedTargetState has ALREADY occurred twice in the game history!
        // Playing preferredMove would immediately draw by threefold repetition (score = 0).
        var history = new List<ulong>
        {
            repeatedTargetState.ZobristHash,
            board.ZobristHash,
            repeatedTargetState.ZobristHash,
            board.ZobristHash
        };

        var moveAvoidingDraw = await ai.GetMoveAsync(board, legalMoves, progress: null, stateHashHistory: history);
        moveAvoidingDraw.Should().NotBe(preferredMove, "Winning AI should avoid walking into a 3-fold repetition draw when other winning moves exist");
    }

    [Fact]
    public async Task MinimaxPlayer_WhenLosing_SeeksThreefoldRepetitionDraw()
    {
        // White has 1 Man vs 3 Black Kings (heavily losing ~ -800)
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(new Position(6, 3), new Piece(PieceColor.White, PieceType.Man));
        board.SetPiece(new Position(1, 2), new Piece(PieceColor.Black, PieceType.King));
        board.SetPiece(new Position(1, 6), new Piece(PieceColor.Black, PieceType.King));
        board.SetPiece(new Position(3, 6), new Piece(PieceColor.Black, PieceType.King));
        board.RecalculateHash();

        var legalMoves = _ruleEngine.GetLegalMoves(board);
        legalMoves.Should().HaveCount(2); // (6,3)->(5,2) and (6,3)->(5,4)

        var ai = new MinimaxPlayer(depth: 4, variant: CheckersVariant.English);
        var normalMove = await ai.GetMoveAsync(board, legalMoves);
        var otherMove = legalMoves.First(m => m != normalMove);
        var drawTargetState = _ruleEngine.ApplyMove(board, otherMove);

        // Seed history so that otherMove immediately triggers a 3-fold repetition draw (score = 0 > -800)
        var history = new List<ulong>
        {
            drawTargetState.ZobristHash,
            board.ZobristHash,
            drawTargetState.ZobristHash,
            board.ZobristHash
        };

        var salvagedMove = await ai.GetMoveAsync(board, legalMoves, progress: null, stateHashHistory: history);
        salvagedMove.Should().Be(otherMove, "Losing AI should choose the move that immediately claims a 0.00 threefold repetition draw");
    }

    [Fact]
    public async Task MinimaxPlayer_SelectivePruning_ReducesNodesComparedToExactSearch()
    {
        var board = BoardState.CreateInitial();
        var legalMoves = _ruleEngine.GetLegalMoves(board);

        var exactAi = new MinimaxPlayer(depth: 8, useTranspositionTable: true)
        {
            UseSelectivePruning = false
        };
        var selectiveAi = new MinimaxPlayer(depth: 8, useTranspositionTable: true)
        {
            UseSelectivePruning = true
        };

        var exactMove = await exactAi.GetMoveAsync(board, legalMoves);
        var selectiveMove = await selectiveAi.GetMoveAsync(board, legalMoves);

        legalMoves.Should().Contain(exactMove);
        legalMoves.Should().Contain(selectiveMove);
        selectiveAi.NodesEvaluated.Should().BeLessThan(exactAi.NodesEvaluated,
            "Verified LMR, RFP, and Futility Pruning should reduce searched nodes at depth 8");
    }

    [Fact]
    public async Task MinimaxPlayer_PartialIterationAdoption_AdoptsBetterRootMoveFoundBeforeTimeout()
    {
        var board = BoardState.CreateInitial();
        var legalMoves = _ruleEngine.GetLegalMoves(board);
        legalMoves.Count.Should().BeGreaterThanOrEqualTo(3);

        // Identify the first two moves in initial root ordering
        var orderedMoves = legalMoves
            .OrderByDescending(m => m.CapturedPositions.Count)
            .ThenByDescending(m => m.IsPromotion ? 1 : 0)
            .ToList();

        var move0 = orderedMoves[0];
        var move1 = orderedMoves[1];

        var evaluator = new PartialIterationTestEvaluator(
            rootMoveCount: orderedMoves.Count,
            sleepMsOnDepth2Move1: 80);

        var ai = new MinimaxPlayer(
            SearchLimits.TimePerMove(TimeSpan.FromMilliseconds(50)),
            evaluator: evaluator,
            useTranspositionTable: false,
            useQuiescence: false)
        {
            UseSelectivePruning = false
        };

        var chosenMove = await ai.GetMoveAsync(board, legalMoves);

        ai.LastSearchAdoptedPartialIteration.Should().BeTrue(
            "Depth 2 timed out on move index 2 after move 0 and move 1 completed cleanly");
        chosenMove.Should().Be(move1,
            "Move 1 beat Move 0 at depth 2 before the clock expired on Move 2, so the partial depth-2 result must be adopted");
        ai.LastAnalysis.Should().NotBeNull();
        ai.LastAnalysis!.Value.Should().Be("+120");
    }

    [Fact]
    public async Task MinimaxPlayer_EarlyRootTermination_ExitsEarlyWhenOneMoveClearlyDominatesInTimedMode()
    {
        // Construct an English Checkers position where White has 3 legal moves with 2 Kings,
        // and only 1 move avoids losing material / dominates all alternatives by >= 150 cp.
        var board = BoardState.CreateEmpty(PieceColor.White);
        board.SetPiece(new Position(6, 1), new Piece(PieceColor.White, PieceType.King));
        board.SetPiece(new Position(7, 6), new Piece(PieceColor.White, PieceType.Man));
        board.SetPiece(new Position(4, 3), new Piece(PieceColor.Black, PieceType.King));
        board.SetPiece(new Position(1, 6), new Piece(PieceColor.Black, PieceType.King));
        board.RecalculateHash();

        var englishRules = new RuleEngine(CheckersVariant.English);
        var legalMoves = englishRules.GetLegalMoves(board);
        legalMoves.Count.Should().BeGreaterThanOrEqualTo(2);

        var timedAi = new MinimaxPlayer(
            SearchLimits.TimePerMove(TimeSpan.FromSeconds(5)),
            ruleEngine: englishRules,
            useTranspositionTable: true,
            useQuiescence: true,
            variant: CheckersVariant.English)
        {
            UseEarlyRootTermination = true
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var chosenMove = await timedAi.GetMoveAsync(board, legalMoves);
        sw.Stop();

        legalMoves.Should().Contain(chosenMove);
        timedAi.LastSearchTerminatedEarly.Should().BeTrue(
            "One root move leads all alternatives by >= 150 cp for consecutive iterations at depth >= 8");
        sw.ElapsedMilliseconds.Should().BeLessThan(1500,
            "Early root termination should stop well before the 3,333ms soft limit on a 5.0s budget");
    }

    private sealed class PartialIterationTestEvaluator : IEvaluationFunction
    {
        private readonly int _rootMoveCount;
        private readonly int _sleepMsOnDepth2Move1;
        private int _calls;

        public PartialIterationTestEvaluator(int rootMoveCount, int sleepMsOnDepth2Move1)
        {
            _rootMoveCount = rootMoveCount;
            _sleepMsOnDepth2Move1 = sleepMsOnDepth2Move1;
        }

        public int Evaluate(BoardState state)
        {
            int callIndex = Interlocked.Increment(ref _calls);

            // Depth 1 leaf evaluations (1..7, ply 1, Black to move; NegaMax negates returned score):
            // Make move0 score +50 (Black returns -50), move1 score +10 (Black returns -10), others 0.
            if (callIndex == 1)
            {
                return -50;
            }
            if (callIndex == 2)
            {
                return -10;
            }
            if (callIndex <= _rootMoveCount)
            {
                return 0;
            }

            // Depth 2 leaf evaluations (ply 2, White to move):
            // Next 7 depth-2 leaf calls belong to move0's replies -> return -40 so move0 drops to -40 at depth 2.
            if (callIndex <= _rootMoveCount * 2)
            {
                return -40;
            }

            // Subsequent depth-2 leaf calls belong to move1's replies -> return +120 and sleep so move2 times out.
            if (callIndex == (_rootMoveCount * 2) + 1)
            {
                Thread.Sleep(_sleepMsOnDepth2Move1);
            }
            return 120;
        }
    }
}
