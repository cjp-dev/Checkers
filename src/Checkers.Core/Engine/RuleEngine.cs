using Checkers.Core.Models;

namespace Checkers.Core.Engine;

/// <summary>
/// Checkers rule engine implementing 8x8 Draughts with Flying Kings,
/// strict mandatory captures, and free choice among capture sequences.
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    private static readonly (int dRow, int dCol)[] AllDiagonals =
    [
        (-1, -1), (-1, 1), (1, -1), (1, 1)
    ];

    public IReadOnlyList<Move> GetLegalMoves(BoardState state)
    {
        var captureMoves = new List<Move>();

        // 1. Scan for captures
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var piece = state.GetPiece(r, c);
                if (piece.HasValue && piece.Value.Color == state.ActivePlayer)
                {
                    var pos = new Position(r, c);
                    if (piece.Value.IsMan)
                    {
                        FindManCaptures(state, pos, pos, [pos], [], captureMoves);
                    }
                    else
                    {
                        FindFlyingKingCaptures(state, pos, pos, [pos], [], captureMoves);
                    }
                }
            }
        }

        // Strict Mandatory Capture: if captures exist, ONLY captures are legal
        if (captureMoves.Count > 0)
        {
            return captureMoves;
        }

        // 2. Generate quiet moves
        var quietMoves = new List<Move>();
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var piece = state.GetPiece(r, c);
                if (piece.HasValue && piece.Value.Color == state.ActivePlayer)
                {
                    var pos = new Position(r, c);
                    if (piece.Value.IsMan)
                    {
                        FindManQuietMoves(state, pos, piece.Value, quietMoves);
                    }
                    else
                    {
                        FindFlyingKingQuietMoves(state, pos, quietMoves);
                    }
                }
            }
        }

        return quietMoves;
    }

    public bool IsLegalMove(BoardState state, Move move)
    {
        var legalMoves = GetLegalMoves(state);
        return legalMoves.Any(m =>
            m.From == move.From &&
            m.To == move.To &&
            m.CapturedPositions.Count == move.CapturedPositions.Count &&
            m.CapturedPositions.SequenceEqual(move.CapturedPositions) &&
            m.Path.SequenceEqual(move.Path));
    }

    public BoardState ApplyMove(BoardState state, Move move)
    {
        var clone = state.Clone();
        var piece = clone.GetPiece(move.From);

        if (!piece.HasValue)
            throw new InvalidOperationException($"No piece exists at start position {move.From}.");

        // Clear original square
        clone.SetPiece(move.From, null);

        // Remove all captured pieces
        foreach (var capPos in move.CapturedPositions)
        {
            clone.SetPiece(capPos, null);
        }

        // Determine promotion
        var finalPiece = (piece.Value.IsMan && move.IsPromotion) ? piece.Value.Crown() : piece.Value;

        // Place piece on destination square
        clone.SetPiece(move.To, finalPiece);

        // Update HalfMoveClock for 40-move draw rule (reset on capture or promotion)
        if (move.IsCapture || move.IsPromotion)
        {
            clone.HalfMoveClock = 0;
        }
        else
        {
            clone.HalfMoveClock++;
        }

        // Update FullMoveNumber after Black moves
        if (clone.ActivePlayer == PieceColor.Black)
        {
            clone.FullMoveNumber++;
        }

        // Switch active turn
        clone.ActivePlayer = clone.ActivePlayer.Opponent();

        // Recalculate state hash
        clone.RecalculateHash();

        return clone;
    }

    public (GameStatus Status, GameOverReason Reason) EvaluateGameStatus(
        BoardState state,
        IReadOnlyList<ulong>? stateHashHistory = null)
    {
        // 1. Check piece elimination
        if (state.ActivePlayer == PieceColor.White && state.WhitePiecesCount == 0)
        {
            return (GameStatus.BlackWon, GameOverReason.OpponentPiecesEliminated);
        }
        if (state.ActivePlayer == PieceColor.Black && state.BlackPiecesCount == 0)
        {
            return (GameStatus.WhiteWon, GameOverReason.OpponentPiecesEliminated);
        }

        // 2. Check no legal moves (blocked)
        var legalMoves = GetLegalMoves(state);
        if (legalMoves.Count == 0)
        {
            return state.ActivePlayer == PieceColor.White
                ? (GameStatus.BlackWon, GameOverReason.OpponentNoLegalMoves)
                : (GameStatus.WhiteWon, GameOverReason.OpponentNoLegalMoves);
        }

        // 3. 40-move rule without capture or promotion (80 half-moves)
        if (state.HalfMoveClock >= 80)
        {
            return (GameStatus.Draw, GameOverReason.FortyMoveRuleWithoutCaptureOrPromotion);
        }

        // 4. Threefold repetition
        if (stateHashHistory != null)
        {
            int repetitions = stateHashHistory.Count(h => h == state.ZobristHash);
            if (repetitions >= 3)
            {
                return (GameStatus.Draw, GameOverReason.ThreefoldRepetition);
            }
        }

        return (GameStatus.InProgress, GameOverReason.None);
    }

    #region Man Move Generation

    private static void FindManQuietMoves(
        BoardState state,
        Position from,
        Piece piece,
        List<Move> quietMoves)
    {
        int forwardRow = piece.Color == PieceColor.White ? -1 : 1;
        int targetRow = piece.Color == PieceColor.White ? 0 : 7;

        foreach (int dCol in new[] { -1, 1 })
        {
            var dest = from.Offset(forwardRow, dCol);
            if (dest.IsValid && dest.IsDarkSquare && state.GetPiece(dest) == null)
            {
                bool isPromotion = dest.Row == targetRow;
                quietMoves.Add(Move.CreateQuiet(from, dest, isPromotion));
            }
        }
    }

    private static void FindManCaptures(
        BoardState state,
        Position initialFrom,
        Position currentPos,
        List<Position> pathSoFar,
        List<Position> capturedSoFar,
        List<Move> resultMoves)
    {
        var playerColor = state.ActivePlayer;
        int forwardRow = playerColor == PieceColor.White ? -1 : 1;
        int crownRow = playerColor == PieceColor.White ? 0 : 7;

        foreach (int dCol in new[] { -1, 1 })
        {
            var jumpedPos = currentPos.Offset(forwardRow, dCol);
            var landingPos = currentPos.Offset(2 * forwardRow, 2 * dCol);

            if (!landingPos.IsValid || !landingPos.IsDarkSquare)
                continue;

            // Landing square must be empty (or start of path if circular jump)
            if (state.GetPiece(landingPos) != null && landingPos != initialFrom)
                continue;

            // Must jump over opponent piece that hasn't been jumped yet in this sequence
            var jumpedPiece = state.GetPiece(jumpedPos);
            if (jumpedPiece.HasValue &&
                jumpedPiece.Value.Color == playerColor.Opponent() &&
                !capturedSoFar.Contains(jumpedPos))
            {
                var newPath = new List<Position>(pathSoFar) { landingPos };
                var newCaptured = new List<Position>(capturedSoFar) { jumpedPos };

                // Promotion rule: reaching crown row promotes man and ends turn immediately
                if (landingPos.Row == crownRow)
                {
                    resultMoves.Add(Move.CreateCapture(
                        initialFrom,
                        landingPos,
                        newPath,
                        newCaptured,
                        isPromotion: true));
                }
                else
                {
                    // Search recursively for further jumps from landingPos
                    int countBefore = resultMoves.Count;
                    FindManCaptures(state, initialFrom, landingPos, newPath, newCaptured, resultMoves);

                    // If no further jumps from landingPos, this landing is the completed capture move
                    if (resultMoves.Count == countBefore)
                    {
                        resultMoves.Add(Move.CreateCapture(
                            initialFrom,
                            landingPos,
                            newPath,
                            newCaptured,
                            isPromotion: false));
                    }
                }
            }
        }
    }

    #endregion

    #region Flying King Move Generation

    private static void FindFlyingKingQuietMoves(
        BoardState state,
        Position from,
        List<Move> quietMoves)
    {
        foreach (var (dRow, dCol) in AllDiagonals)
        {
            int step = 1;
            while (true)
            {
                var dest = from.Offset(step * dRow, step * dCol);
                if (!dest.IsValid || !dest.IsDarkSquare)
                    break;

                if (state.GetPiece(dest) != null)
                    break; // Blocked by any piece

                quietMoves.Add(Move.CreateQuiet(from, dest, isPromotion: false));
                step++;
            }
        }
    }

    private static void FindFlyingKingCaptures(
        BoardState state,
        Position initialFrom,
        Position currentPos,
        List<Position> pathSoFar,
        List<Position> capturedSoFar,
        List<Move> resultMoves)
    {
        var playerColor = state.ActivePlayer;

        foreach (var (dRow, dCol) in AllDiagonals)
        {
            int step = 1;
            Position? enemyToJump = null;
            var landingSquares = new List<Position>();

            while (true)
            {
                var scanPos = currentPos.Offset(step * dRow, step * dCol);
                if (!scanPos.IsValid || !scanPos.IsDarkSquare)
                    break;

                var pieceOnScan = state.GetPiece(scanPos);

                if (enemyToJump == null)
                {
                    if (pieceOnScan == null)
                    {
                        // Empty square before reaching enemy - continue flying along ray
                        step++;
                        continue;
                    }

                    if (pieceOnScan.Value.Color == playerColor || capturedSoFar.Contains(scanPos))
                    {
                        // Friendly piece or already jumped piece blocks the ray
                        break;
                    }

                    // Found un-jumped opponent piece
                    enemyToJump = scanPos;
                    step++;
                }
                else
                {
                    // Already passed the enemy piece: looking for landing squares beyond it
                    if (pieceOnScan != null && scanPos != initialFrom)
                    {
                        // Blocked by another piece beyond the jumped enemy
                        break;
                    }

                    // scanPos is an empty square beyond the enemy piece - valid landing square candidate
                    landingSquares.Add(scanPos);
                    step++;
                }
            }

            if (enemyToJump != null && landingSquares.Count > 0)
            {
                var rayMoves = new List<Move>();
                bool anyContinuation = false;

                foreach (var landingPos in landingSquares)
                {
                    var newPath = new List<Position>(pathSoFar) { landingPos };
                    var newCaptured = new List<Position>(capturedSoFar) { enemyToJump.Value };

                    int countBefore = rayMoves.Count;
                    FindFlyingKingCaptures(state, initialFrom, landingPos, newPath, newCaptured, rayMoves);

                    if (rayMoves.Count > countBefore)
                    {
                        anyContinuation = true;
                    }
                }

                if (anyContinuation)
                {
                    // Must continue capture sequence: keep only extended chains
                    resultMoves.AddRange(rayMoves);
                }
                else
                {
                    // No landing square allows further jumping: all landing squares are valid terminal stops
                    foreach (var landingPos in landingSquares)
                    {
                        var newPath = new List<Position>(pathSoFar) { landingPos };
                        var newCaptured = new List<Position>(capturedSoFar) { enemyToJump.Value };

                        resultMoves.Add(Move.CreateCapture(
                            initialFrom,
                            landingPos,
                            newPath,
                            newCaptured,
                            isPromotion: false));
                    }
                }
            }
        }
    }

    #endregion
}
