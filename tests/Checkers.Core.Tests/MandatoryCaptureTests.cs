using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class MandatoryCaptureTests
{
    private readonly IRuleEngine _engine;

    public MandatoryCaptureTests() : this(new RuleEngine()) { }

    protected MandatoryCaptureTests(IRuleEngine engine) => _engine = engine;

    [Fact]
    public void MandatoryCapture_ExcludesQuietMoves_WhenCaptureExists()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        // White man at (4, 3)
        var whitePos = new Position(4, 3);
        board.SetPiece(whitePos, new Piece(PieceColor.White, PieceType.Man));

        // Black man at (3, 2) that can be jumped into (2, 1)
        var blackPos = new Position(3, 2);
        board.SetPiece(blackPos, new Piece(PieceColor.Black, PieceType.Man));

        // Another White man at (5, 6) that could make quiet moves to (4, 5) or (4, 7)
        var quietWhitePos = new Position(5, 6);
        board.SetPiece(quietWhitePos, new Piece(PieceColor.White, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // Strict mandatory capture: only the jump should be returned
        moves.Should().HaveCount(1);
        var jump = moves[0];
        jump.From.Should().Be(whitePos);
        jump.To.Should().Be(new Position(2, 1));
        jump.IsCapture.Should().BeTrue();
        jump.CapturedPositions.Should().ContainSingle().Which.Should().Be(blackPos);
    }

    [Fact]
    public void MandatoryCapture_OffersFreeChoice_BetweenMultipleCapturePaths()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);

        // Piece 1: White at (4, 3) can jump Black at (3, 2) -> landing at (2, 1)
        var white1 = new Position(4, 3);
        board.SetPiece(white1, new Piece(PieceColor.White, PieceType.Man));
        var black1 = new Position(3, 2);
        board.SetPiece(black1, new Piece(PieceColor.Black, PieceType.Man));

        // Piece 2: White at (5, 6) can jump Black at (4, 5) -> landing at (3, 4)
        var white2 = new Position(5, 6);
        board.SetPiece(white2, new Piece(PieceColor.White, PieceType.Man));
        var black2 = new Position(4, 5);
        board.SetPiece(black2, new Piece(PieceColor.Black, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // Both captures must be available
        moves.Should().HaveCount(2);
        moves.Should().AllSatisfy(m => m.IsCapture.Should().BeTrue());
        moves.Select(m => m.From).Should().BeEquivalentTo(new[] { white1, white2 });
    }

    [Fact]
    public void CannotJumpFriendlyPieces()
    {
        var board = BoardState.CreateEmpty(PieceColor.White);
        var white1 = new Position(4, 3);
        var white2 = new Position(3, 2);
        board.SetPiece(white1, new Piece(PieceColor.White, PieceType.Man));
        board.SetPiece(white2, new Piece(PieceColor.White, PieceType.Man));

        var moves = _engine.GetLegalMoves(board);

        // Cannot jump friendly piece; (4,3) can only quietly move to (3,4)
        moves.Should().NotContain(m => m.IsCapture);
        moves.Should().Contain(m => m.From == white1 && m.To == new Position(3, 4));
    }
}
