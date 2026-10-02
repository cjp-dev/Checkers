using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class GameRecordFormatTests
{
    [Fact]
    public void Format_EmptySession_ReturnsEmptyString()
    {
        var session = new GameSession();
        string text = GameRecordFormat.Format(session);
        text.Should().BeEmpty();
    }

    [Fact]
    public void Format_PlayedMoves_ReturnsSpaceSeparatedMoves()
    {
        var session = new GameSession();

        // Move 1: White
        var move1 = session.LegalMoves.First();
        session.TryMakeMove(move1);

        // Move 2: Black 20-24 (Black pieces are 1-12 in standard draughts; wait, let's pick a valid move from LegalMoves)
        var move2 = session.LegalMoves.First();
        session.TryMakeMove(move2);

        string text = GameRecordFormat.Format(session);
        text.Should().Be($"{move1.Notation} {move2.Notation}");
    }

    [Fact]
    public void Format_WithTags_OutputsHeaderTagsAndMoves()
    {
        var session = new GameSession();
        var move1 = session.LegalMoves.First();
        session.TryMakeMove(move1);

        var tags = new Dictionary<string, string>
        {
            ["GameMode"] = "HumanVsComputer",
            ["Difficulty"] = "Medium"
        };

        string formatted = GameRecordFormat.Format(session, tags);

        formatted.Should().Contain("[GameMode \"HumanVsComputer\"]");
        formatted.Should().Contain("[Difficulty \"Medium\"]");
        formatted.Should().EndWith(move1.Notation);
    }

    [Fact]
    public void Parse_EmptyOrWhitespace_ReturnsInitialSession()
    {
        var record = GameRecordFormat.Parse("   \r\n   ");
        record.Session.MoveHistory.Should().BeEmpty();
        record.Session.CurrentState.ActivePlayer.Should().Be(PieceColor.White);
        record.Session.Status.Should().Be(GameStatus.InProgress);
    }

    [Fact]
    public void Parse_RoundTrip_RestoresPositionAndMoves()
    {
        var session = new GameSession();

        // Play 3 moves
        var m1 = session.LegalMoves.First();
        session.TryMakeMove(m1);

        var m2 = session.LegalMoves.First();
        session.TryMakeMove(m2);

        var m3 = session.LegalMoves.First();
        session.TryMakeMove(m3);

        string formatted = GameRecordFormat.Format(session);
        var loadedRecord = GameRecordFormat.Parse(formatted);

        loadedRecord.Session.MoveHistory.Should().HaveCount(3);
        loadedRecord.Session.MoveHistory[0].Notation.Should().Be(m1.Notation);
        loadedRecord.Session.MoveHistory[1].Notation.Should().Be(m2.Notation);
        loadedRecord.Session.MoveHistory[2].Notation.Should().Be(m3.Notation);
        loadedRecord.Session.CurrentState.ActivePlayer.Should().Be(session.CurrentState.ActivePlayer);
        loadedRecord.Session.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Parse_WithMoveNumbersCommentsAndTags_ParsesSuccessfully()
    {
        var session = new GameSession();
        var m1 = session.LegalMoves.First();
        session.TryMakeMove(m1);
        var m2 = session.LegalMoves.First();
        session.TryMakeMove(m2);

        string text = $$"""
            [Event "Test Match"]
            [GameMode "HumanVsComputer"]
            [Difficulty "Hard"]

            ; Opening comment
            1. {{m1.Notation}} {Strong move} 1... {{m2.Notation}} *
            """;

        var record = GameRecordFormat.Parse(text);

        record.GetTag("Event").Should().Be("Test Match");
        record.GetTag("GameMode").Should().Be("HumanVsComputer");
        record.GetTag("Difficulty").Should().Be("Hard");

        record.Session.MoveHistory.Should().HaveCount(2);
        record.Session.MoveHistory[0].Notation.Should().Be(m1.Notation);
        record.Session.MoveHistory[1].Notation.Should().Be(m2.Notation);
    }

    [Fact]
    public void Parse_InvalidMove_ThrowsFormatException()
    {
        string text = "1-32 99-99";
        var act = () => GameRecordFormat.Parse(text);

        act.Should().Throw<FormatException>()
            .WithMessage("*not a legal move*");
    }

    [Fact]
    public void Parse_MoveAfterGameOver_ThrowsFormatException()
    {
        // Custom board where White has 1 piece capturing Black's only piece to win
        var custom = BoardState.CreateEmpty(PieceColor.White);
        custom.SetPiece(new Position(4, 3), new Piece(PieceColor.White, PieceType.Man)); // #18
        custom.SetPiece(new Position(3, 2), new Piece(PieceColor.Black, PieceType.Man)); // #14
        // Legal move: (4,3) jumps (3,2) -> (2,1) = 18x9

        var session = new GameSession();
        session.StartFromState(custom);

        var winningMove = session.LegalMoves.Single();
        session.TryMakeMove(winningMove);
        session.Status.Should().Be(GameStatus.WhiteWon);

        var parseSession = new GameSession();
        parseSession.StartFromState(custom);

        // Parsing the winning move and then an extra move should throw FormatException
        var act = () => GameRecordFormat.Parse($"{winningMove.Notation} 31-26", parseSession);

        act.Should().Throw<FormatException>()
            .WithMessage("*already over*");
    }

    [Fact]
    public void Parse_CaptureNotationSeparators_ParsesDashColonAndX()
    {
        var custom = BoardState.CreateEmpty(PieceColor.White);
        custom.SetPiece(new Position(4, 3), new Piece(PieceColor.White, PieceType.Man)); // #18
        custom.SetPiece(new Position(3, 2), new Piece(PieceColor.Black, PieceType.Man)); // #14
        // Legal move is #18 jumping #14 to #9

        // Test with "18:9"
        var s1 = new GameSession();
        s1.StartFromState(custom);
        var r1 = GameRecordFormat.Parse("18:9", s1);
        r1.Session.MoveHistory.Should().HaveCount(1);

        // Test with "18-9"
        var s2 = new GameSession();
        s2.StartFromState(custom);
        var r2 = GameRecordFormat.Parse("18-9", s2);
        r2.Session.MoveHistory.Should().HaveCount(1);

        // Test with "18x9"
        var s3 = new GameSession();
        s3.StartFromState(custom);
        var r3 = GameRecordFormat.Parse("18x9", s3);
        r3.Session.MoveHistory.Should().HaveCount(1);
    }
}
