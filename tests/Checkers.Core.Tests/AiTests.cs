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
}
