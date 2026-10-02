using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Default static heuristic evaluation scoring material, advancement, center control, and back-rank defense.
/// Returns scores relative to the active player.
/// </summary>
public sealed class EvaluationFunction : IEvaluationFunction
{
    public const int ManValue = 100;
    public const int KingValue = 300;
    public const int CenterControlBonus = 12;
    public const int BackRankDefenseBonus = 15;
    public const int AdvancementStepBonus = 5;

    public int Evaluate(BoardState state)
    {
        int whiteScore = 0;
        int blackScore = 0;

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var piece = state.GetPiece(r, c);
                if (!piece.HasValue)
                    continue;

                int score = 0;

                // 1. Material
                score += piece.Value.IsKing ? KingValue : ManValue;

                // 2. Center Control (Squares #14, #15, #18, #19: rows 3-4, cols 2-5)
                if (r is >= 3 and <= 4 && c is >= 2 and <= 5)
                {
                    score += CenterControlBonus;
                }

                if (piece.Value.Color == PieceColor.White)
                {
                    if (piece.Value.IsMan)
                    {
                        // Advancement bonus for White (closer to Row 0)
                        score += (7 - r) * AdvancementStepBonus;

                        // Back rank defense (preventing Black kinging)
                        if (r == 7)
                        {
                            score += BackRankDefenseBonus;
                        }
                    }
                    whiteScore += score;
                }
                else
                {
                    if (piece.Value.IsMan)
                    {
                        // Advancement bonus for Black (closer to Row 7)
                        score += r * AdvancementStepBonus;

                        // Back rank defense (preventing White kinging)
                        if (r == 0)
                        {
                            score += BackRankDefenseBonus;
                        }
                    }
                    blackScore += score;
                }
            }
        }

        // Return score relative to ActivePlayer
        int netScore = whiteScore - blackScore;
        return state.ActivePlayer == PieceColor.White ? netScore : -netScore;
    }
}
