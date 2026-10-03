using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class PromotionTests
{
    private readonly IRuleEngine _engine;

    public PromotionTests() : this(new RuleEngine()) { }

    protected PromotionTests(IRuleEngine engine) => _engine = engine;

    [Fact]
    public void QuietMove_ReachingBackRank_PromotesToKing()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        var whitePos = new Position(1, 2);
        board.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);
        moves.Should().AllSatisfy(m => m.IsPromotion.Should().BeTrue());

        var chosenMove = moves.First(m => m.To == new Position(0, 1));
        var nextState = _engine.ApplyMove(board, chosenMove);

        var crownedPiece = nextState.GetPiece(new Position(0, 1));
        crownedPiece.Should().NotBeNull();
        crownedPiece!.Value.IsKing.Should().BeTrue();
        crownedPiece.Value.Color.Should().Be(PieceColor.White);
        nextState.WhiteKingsCount.Should().Be(1);
    }

    [Fact]
    public void BlackMan_ReachingRow7_PromotesToKing()
    {
        var board = BoardState.CreateEmpty(PieceColor.Black);
        var blackPos = new Position(6, 1);
        board.SetPiece(blackPos, new Piece(PieceColor.Black, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);
        moves.Should().AllSatisfy(m => m.IsPromotion.Should().BeTrue());

        var chosenMove = moves.First(m => m.To == new Position(7, 0));
        var nextState = _engine.ApplyMove(board, chosenMove);

        var crownedPiece = nextState.GetPiece(new Position(7, 0));
        crownedPiece.Should().NotBeNull();
        crownedPiece!.Value.IsKing.Should().BeTrue();
        crownedPiece.Value.Color.Should().Be(PieceColor.Black);
        nextState.BlackKingsCount.Should().Be(1);
    }

    [Fact]
    public void Man_ReachingCrownRowOnJump_PromotesAndEndsTurnImmediately()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);

        // White man at (2, 3)
        var whitePos = new Position(2, 3);
        board.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));

        // Black piece at (1, 2) -> jump lands at (0, 1) (Crown row)
        var black1 = new Position(1, 2);
        board.SetPiece(black1, new Piece(PieceColor.Black, PieceType.Man));

        // Place another Black piece at (1, 2) or elsewhere that a flying king could jump
        // e.g. Black piece at (1, 2) is jumped; if White were a flying king, maybe it could jump (1, 0)?
        // Place a Black piece at (2, 3)... wait, from (0, 1) diagonal is (1, 2) which was just jumped!
        // Another diagonal is (1, 0) and behind it (2, -1 which is invalid),
        // or place an enemy at (1, 2) was jumped. Place enemy at (2, 3)?
        // Let's place an enemy at (2, 3) or (3, 4).
        var black2 = new Position(3, 4);
        board.SetPiece(black2, new Piece(PieceColor.Black, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // The move MUST terminate at (0, 1) with IsPromotion = true
        moves.Should().HaveCount(1);
        var move = moves[0];
        move.To.Should().Be(new Position(0, 1));
        move.IsPromotion.Should().BeTrue();
        move.CapturedPositions.Should().ContainSingle().Which.Should().Be(black1);

        var nextState = _engine.ApplyMove(board, move);
        nextState.GetPiece(new Position(0, 1))!.Value.IsKing.Should().BeTrue();
        nextState.ActivePlayer.Should().Be(PieceColor.Black);
    }
}
