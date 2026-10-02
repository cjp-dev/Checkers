using Checkers.Core.Engine;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.Core.Tests;

public class BoardStateTests
{
    [Fact]
    public void InitialBoard_HasCorrectPieceCounts()
    {
        var board = BoardState.CreateInitial();

        board.WhitePiecesCount.Should().Be(12);
        board.BlackPiecesCount.Should().Be(12);
        board.WhiteKingsCount.Should().Be(0);
        board.BlackKingsCount.Should().Be(0);
        board.ActivePlayer.Should().Be(PieceColor.White);
        board.HalfMoveClock.Should().Be(0);
        board.FullMoveNumber.Should().Be(1);
    }

    [Fact]
    public void InitialBoard_PiecesAreOnDarkSquares()
    {
        var board = BoardState.CreateInitial();

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var pos = new Position(r, c);
                var piece = board.GetPiece(pos);

                if (pos.IsDarkSquare)
                {
                    if (r <= 2)
                    {
                        piece.Should().NotBeNull();
                        piece!.Value.Color.Should().Be(PieceColor.Black);
                        piece.Value.Type.Should().Be(PieceType.Man);
                    }
                    else if (r >= 5)
                    {
                        piece.Should().NotBeNull();
                        piece!.Value.Color.Should().Be(PieceColor.White);
                        piece.Value.Type.Should().Be(PieceType.Man);
                    }
                    else
                    {
                        piece.Should().BeNull();
                    }
                }
                else
                {
                    piece.Should().BeNull();
                }
            }
        }
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(4, 0, 7)]
    [InlineData(5, 1, 0)]
    [InlineData(12, 2, 7)]
    [InlineData(13, 3, 0)]
    [InlineData(20, 4, 7)]
    [InlineData(21, 5, 0)]
    [InlineData(28, 6, 7)]
    [InlineData(29, 7, 0)]
    [InlineData(32, 7, 6)]
    public void DraughtsIndexMapping_MapsBidirectionally(int draughtsIndex, int expectedRow, int expectedCol)
    {
        var pos = Position.FromDraughtsIndex(draughtsIndex);
        pos.Row.Should().Be(expectedRow);
        pos.Col.Should().Be(expectedCol);
        pos.ToDraughtsIndex().Should().Be(draughtsIndex);
    }

    [Fact]
    public void DraughtsIndex_All32SquaresMapDistinctly()
    {
        var positions = new HashSet<Position>();
        for (int i = 1; i <= 32; i++)
        {
            var pos = Position.FromDraughtsIndex(i);
            pos.IsDarkSquare.Should().BeTrue();
            positions.Add(pos).Should().BeTrue();
            pos.ToDraughtsIndex().Should().Be(i);
        }

        positions.Count.Should().Be(32);
    }

    [Fact]
    public void BoardClone_ProducesIndependentState()
    {
        var board = BoardState.CreateInitial();
        var clone = board.Clone();

        clone.WhitePiecesCount.Should().Be(board.WhitePiecesCount);
        clone.ZobristHash.Should().Be(board.ZobristHash);

        var pos = new Position(5, 0);
        clone.SetPiece(pos, null);

        clone.WhitePiecesCount.Should().Be(11);
        board.WhitePiecesCount.Should().Be(12);
        board.GetPiece(pos).Should().NotBeNull();
    }
}
