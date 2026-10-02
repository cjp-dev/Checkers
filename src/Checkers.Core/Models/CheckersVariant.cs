namespace Checkers.Core.Models;

/// <summary>
/// Supported Checkers / Draughts game variants.
/// </summary>
public enum CheckersVariant
{
    /// <summary>
    /// International Draughts rules with Flying Kings (kings can slide and jump across open diagonals).
    /// </summary>
    International,

    /// <summary>
    /// English Checkers (American Draughts / Straight Checkers):
    /// Kings move exactly 1 square diagonally in 4 directions, jump adjacent enemy pieces landing 1 square behind in 4 directions,
    /// and chain single-hop multi-jumps. Regular men move and jump forward only.
    /// </summary>
    English
}
