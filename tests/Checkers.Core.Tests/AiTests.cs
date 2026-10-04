using Checkers.Core.AI;
using Checkers.Core.Bitboards;
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

    [Theory]
    [InlineData(CheckersVariant.English)]
    [InlineData(CheckersVariant.International)]
    public void EvaluationFunction_ColorFlipSymmetry_HoldsAcrossDiversePositions(CheckersVariant variant)
    {
        var eval = new EvaluationFunction(variant);
        var legacyEval = new LegacyEvaluationFunction(variant);

        // Test positions covering openings, middlegames, structural patterns, runaway cones, and king endgames
        (ulong wm, ulong bm, ulong wk, ulong bk)[] testMasks =
        [
            // Initial position
            (0xAA55AA0000000000UL, 0x000000000055AA55UL, 0UL, 0UL),
            // Asymmetric middlegame with structural patterns (Bridge, Triangle, Oreo, Dog, Right Lock)
            (
                (1UL << 58) | (1UL << 62) | (1UL << 51) | (1UL << 56) | (1UL << 49) | (1UL << 42) | (1UL << 35),
                (1UL << 1) | (1UL << 5) | (1UL << 12) | (1UL << 14) | (1UL << 21) | (1UL << 28),
                0UL,
                0UL
            ),
            // Runaway checker + tail pin endgame
            (
                (1UL << 19) | (1UL << 51),
                (1UL << 49) | (1UL << 10),
                (1UL << 42),
                0UL
            ),
            // Multi-king endgame with single-corner and short-corner kings
            (
                (1UL << 44),
                (1UL << 21),
                (1UL << 7) | (1UL << 35) | (1UL << 62),
                (1UL << 56) | (1UL << 28) | (1UL << 6)
            )
        ];

        foreach (var (wm, bm, wk, bk) in testMasks)
        {
            var posWhite = new BitPosition
            {
                WhiteMen = wm,
                BlackMen = bm,
                WhiteKings = wk,
                BlackKings = bk,
                SideToMove = PieceColor.White
            };

            var posSwappedBlack = new BitPosition
            {
                WhiteMen = Flip180(bm),
                BlackMen = Flip180(wm),
                WhiteKings = Flip180(bk),
                BlackKings = Flip180(wk),
                SideToMove = PieceColor.Black
            };

            int scoreWhite = eval.Evaluate(in posWhite);
            int scoreSwappedBlack = eval.Evaluate(in posSwappedBlack);

            // Evaluate(in pos) returns score from SideToMove's perspective!
            // Wait: when we flip 180 degrees and swap colors AND swap SideToMove from White to Black,
            // Black in the flipped position has the exact mirror of White's pieces in the original position!
            // Therefore, from SideToMove's perspective, scoreSwappedBlack MUST equal scoreWhite!
            // And if SideToMove is kept as White on the flipped position, its score MUST equal -scoreWhite!
            scoreSwappedBlack.Should().Be(scoreWhite);

            var posSwappedWhiteToMove = posSwappedBlack;
            posSwappedWhiteToMove.SideToMove = PieceColor.White;
            eval.Evaluate(in posSwappedWhiteToMove).Should().Be(-scoreWhite);

            // Verify legacy evaluator symmetry as well
            legacyEval.Evaluate(in posSwappedBlack).Should().Be(legacyEval.Evaluate(in posWhite));
            legacyEval.Evaluate(in posSwappedWhiteToMove).Should().Be(-legacyEval.Evaluate(in posWhite));
        }
    }

    [Fact]
    public void EvaluationFunction_RunawayCone_AwardsBonusOnlyWhenForwardConeIsClearAndNoEnemyKings()
    {
        var eval = new EvaluationFunction(CheckersVariant.English);

        // White Man at (3, 6) [sq 30, row 3]: forward cone on rows 0..2 is cols 3..7.
        // Black Man at (2, 1) [sq 17, row 2] is on row 2 ahead of White's row 3?
        // Wait: board_eval.c requires NO enemy pieces on ANY row ahead of White's row (first_p2_row >= r_w),
        // AND no pieces inside the forward cone!
        // So if White Man is at (2, 3) [sq 19, row 2] and Black has 2 Men at (3, 0) [sq 24] and (4, 1) [sq 33]
        // (where (3,0) is blocked by (4,1) so Black is not a runaway), White at (2, 3) IS a runaway (+40 + 6*5 = +70 cp)!
        var runawayBoard = BoardState.CreateEmpty(PieceColor.White);
        runawayBoard.SetPiece(new Position(2, 3), Piece.WhiteMan);
        runawayBoard.SetPiece(new Position(6, 1), Piece.WhiteMan); // blocks Black's cone
        runawayBoard.SetPiece(new Position(3, 0), Piece.BlackMan);
        runawayBoard.SetPiece(new Position(4, 7), Piece.BlackMan);

        // Now move Black's Man from (3, 0) to (1, 2) [inside White's forward cone ahead of row 2]:
        var blockedBoard = BoardState.CreateEmpty(PieceColor.White);
        blockedBoard.SetPiece(new Position(2, 3), Piece.WhiteMan);
        blockedBoard.SetPiece(new Position(6, 1), Piece.WhiteMan);
        blockedBoard.SetPiece(new Position(1, 2), Piece.BlackMan);
        blockedBoard.SetPiece(new Position(4, 7), Piece.BlackMan);

        int scoreRunaway = eval.Evaluate(runawayBoard);
        int scoreBlocked = eval.Evaluate(blockedBoard);

        // Runaway board awards +70 cp for White's unstoppable passer at (2, 3)
        scoreRunaway.Should().BeGreaterThan(scoreBlocked + 50);
    }

    [Fact]
    public void EvaluationFunction_TailPin_And_StructuralPatterns_ScoreCorrectly()
    {
        var eval = new EvaluationFunction(CheckersVariant.English);

        // Test White Bridge at (7, 2) [sq 58] and (7, 6) [sq 62] vs non-bridge (7, 4) [sq 60] and (7, 6) [sq 62]
        // Note: (7, 2), (7, 4), and (7, 6) all have the exact same Man PST weight (+6 cp in WhiteManPst3Mask)
        var bridgeBoard = BoardState.CreateEmpty(PieceColor.White);
        bridgeBoard.SetPiece(new Position(7, 2), Piece.WhiteMan);
        bridgeBoard.SetPiece(new Position(7, 6), Piece.WhiteMan);
        bridgeBoard.SetPiece(new Position(0, 1), Piece.BlackMan);
        bridgeBoard.SetPiece(new Position(0, 3), Piece.BlackMan);

        var nonBridgeBoard = BoardState.CreateEmpty(PieceColor.White);
        nonBridgeBoard.SetPiece(new Position(7, 4), Piece.WhiteMan);
        nonBridgeBoard.SetPiece(new Position(7, 6), Piece.WhiteMan);
        nonBridgeBoard.SetPiece(new Position(0, 1), Piece.BlackMan);
        nonBridgeBoard.SetPiece(new Position(0, 3), Piece.BlackMan);

        // Both (7,2) and (7,4) have +6 cp PST and identical row 7 advancement, so the delta is purely the Bridge bonus (+30 cp)!
        (eval.Evaluate(bridgeBoard) - eval.Evaluate(nonBridgeBoard)).Should().Be(30);

        // Test Tail Pin: White King at (4, 1) [sq 33] pinning two lined-up Black Men at (5, 2) [sq 42] and (6, 3) [sq 51] along +9/+18
        var tailPinBoard = BoardState.CreateEmpty(PieceColor.White);
        tailPinBoard.SetPiece(new Position(4, 1), Piece.WhiteKing);
        tailPinBoard.SetPiece(new Position(5, 2), Piece.BlackMan);
        tailPinBoard.SetPiece(new Position(6, 3), Piece.BlackMan);

        // Compare against moving White King from (4, 1) [sq 33, 0 PST] to (4, 7) [sq 39, 0 PST] where it does not tail-pin the two Black men
        var unpinnedBoard = BoardState.CreateEmpty(PieceColor.White);
        unpinnedBoard.SetPiece(new Position(4, 7), Piece.WhiteKing);
        unpinnedBoard.SetPiece(new Position(5, 2), Piece.BlackMan);
        unpinnedBoard.SetPiece(new Position(6, 3), Piece.BlackMan);

        // Both (4,1) and (4,7) have 0 King PST; (4,1) gets +10 cp for the tail pin!
        (eval.Evaluate(tailPinBoard) - eval.Evaluate(unpinnedBoard)).Should().Be(10);
    }

    [Fact]
    public void EvaluationFunction_FlyingKing_RewardsMainDiagonalAndPenalizesShortCorners()
    {
        var intlEval = new EvaluationFunction(CheckersVariant.International);

        // White Flying King on main diagonal (3, 4) [sq 28] vs cramped 2-square short corner (0, 1) [sq 1]
        var mainDiagBoard = BoardState.CreateEmpty(PieceColor.White);
        mainDiagBoard.SetPiece(new Position(3, 4), Piece.WhiteKing);
        mainDiagBoard.SetPiece(new Position(4, 3), Piece.BlackKing);

        var shortCornerBoard = BoardState.CreateEmpty(PieceColor.White);
        shortCornerBoard.SetPiece(new Position(0, 1), Piece.WhiteKing);
        shortCornerBoard.SetPiece(new Position(4, 3), Piece.BlackKing);

        // Main diagonal king at (3, 4) matches Black's main diagonal king at (4, 3) -> net 0
        // Short corner king at (0, 1) has -20 (FlyingKingShortCornerMask) + 4 (2 open rays * 2) = -16 positional vs Black's +20 -> net -36
        intlEval.Evaluate(mainDiagBoard).Should().Be(0);
        intlEval.Evaluate(shortCornerBoard).Should().Be(-36);
    }

    private static ulong Flip180(ulong mask)
    {
        ulong result = 0UL;
        while (mask != 0)
        {
            int sq = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            result |= 1UL << (63 - sq);
        }
        return result;
    }
}
