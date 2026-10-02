namespace Checkers.Core.Models;

/// <summary>
/// Status of a checkers game.
/// </summary>
public enum GameStatus
{
    InProgress,
    WhiteWon,
    BlackWon,
    Draw
}

/// <summary>
/// Detailed reason for game termination.
/// </summary>
public enum GameOverReason
{
    None,
    OpponentPiecesEliminated,
    OpponentNoLegalMoves,
    ThreefoldRepetition,
    FortyMoveRuleWithoutCaptureOrPromotion,
    MutualAgreement
}
