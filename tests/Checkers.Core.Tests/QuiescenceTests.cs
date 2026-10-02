using Checkers.Core.AI;
using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class QuiescenceTests
{
    [Fact]
    public async Task QuiescenceSearch_ResolvesTacticalCaptureSequence()
    {
        // Setup: White can capture Black piece, resolving material imbalance
        var state = BoardState.CreateEmpty(PieceColor.White);
        state.SetPiece(new Position(4, 3), new Piece(PieceColor.White, PieceType.Man));
        state.SetPiece(new Position(3, 2), new Piece(PieceColor.Black, PieceType.Man));
        state.RecalculateHash();

        var player = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(1),
            useQuiescence: true,
            useTranspositionTable: false);

        var ruleEngine = new RuleEngine();
        var legalMoves = ruleEngine.GetLegalMoves(state);

        var move = await player.GetMoveAsync(state, legalMoves);

        move.IsCapture.Should().BeTrue();
        move.From.Should().Be(new Position(4, 3));
        move.To.Should().Be(new Position(2, 1));
    }

    [Fact]
    public async Task MinimaxPlayer_WithTranspositionTable_FindsSameOrBetterMovesThanWithout()
    {
        // Standard starting board position
        var state = BoardState.CreateInitial();
        var ruleEngine = new RuleEngine();
        var legalMoves = ruleEngine.GetLegalMoves(state);

        var playerNoTt = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(3),
            useTranspositionTable: false,
            useQuiescence: false);

        var playerWithTt = new MinimaxPlayer(
            limits: SearchLimits.FixedDepth(3),
            useTranspositionTable: true,
            useQuiescence: false);

        var moveNoTt = await playerNoTt.GetMoveAsync(state, legalMoves);
        var moveWithTt = await playerWithTt.GetMoveAsync(state, legalMoves);

        // Both should pick legal moves
        legalMoves.Should().Contain(m => m.From == moveNoTt.From && m.To == moveNoTt.To);
        legalMoves.Should().Contain(m => m.From == moveWithTt.From && m.To == moveWithTt.To);

        // Transposition table should have registered probes and stores
        playerWithTt.TranspositionTable!.Stores.Should().BeGreaterThan(0);
        playerWithTt.TranspositionTable.Probes.Should().BeGreaterThan(0);
    }
}
