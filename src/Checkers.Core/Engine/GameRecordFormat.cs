using System.Text;
using System.Text.RegularExpressions;
using Checkers.Core.Models;

namespace Checkers.Core.Engine;

/// <summary>
/// Formats and parses checkers games as human-readable text move records with optional metadata tags.
/// Compatible with Portable Draughts Notation (PDN) move lists and space-separated notations.
/// </summary>
public static partial class GameRecordFormat
{
    [GeneratedRegex(@"^\s*\[\s*(\w+)\s+""([^""]*)""\s*\]\s*$", RegexOptions.Multiline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex CurlyCommentRegex();

    [GeneratedRegex(@";.*$", RegexOptions.Multiline)]
    private static partial Regex SemicolonCommentRegex();

    [GeneratedRegex(@"^\d+(\.+)?$")]
    private static partial Regex MoveNumberRegex();

    /// <summary>
    /// Writes moves played up to the current position separated by spaces, with optional header tags.
    /// Undone moves are not included.
    /// </summary>
    public static string Format(GameSession session, IReadOnlyDictionary<string, string>? tags = null)
    {
        var sb = new StringBuilder();

        if (tags != null && tags.Count > 0)
        {
            var mergedTags = new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase);
            if (!mergedTags.ContainsKey("Variant"))
            {
                mergedTags["Variant"] = session.RuleEngine.Variant == CheckersVariant.English ? "English" : "International";
            }

            foreach (var kvp in mergedTags)
            {
                sb.AppendLine($"[{kvp.Key} \"{kvp.Value}\"]");
            }
            sb.AppendLine();
        }

        sb.Append(string.Join(' ', session.MoveHistory.Select(m => m.Notation)));
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Convenience helper to parse a game record and return the replayed GameSession.
    /// </summary>
    public static GameSession ParseSession(string text, GameSession? session = null) => Parse(text, session).Session;

    /// <summary>
    /// Parses a game record, replaying all valid moves into a GameSession.
    /// Supports tags, move numbers, comments, and varied move separators (-, x, X, :, *).
    /// </summary>
    /// <exception cref="FormatException">Thrown when a move is illegal, malformed, or played after game over.</exception>
    public static GameRecord Parse(string text, GameSession? session = null)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Extract metadata tags
        var tagMatches = TagRegex().Matches(text);
        foreach (Match match in tagMatches)
        {
            string key = match.Groups[1].Value;
            string value = match.Groups[2].Value;
            tags[key] = value;
        }

        if (session == null)
        {
            var variant = tags.TryGetValue("Variant", out var varStr) && varStr.Equals("English", StringComparison.OrdinalIgnoreCase)
                ? CheckersVariant.English
                : CheckersVariant.International;
            session = new GameSession(new RuleEngine(variant));
        }

        // 2. Strip tags from text
        string content = TagRegex().Replace(text, string.Empty);

        // 3. Strip comments
        content = CurlyCommentRegex().Replace(content, string.Empty);
        content = SemicolonCommentRegex().Replace(content, string.Empty);

        // 4. Tokenize
        var rawTokens = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int moveNumber = 0;

        foreach (var rawToken in rawTokens)
        {
            string token = rawToken.Trim();

            // Skip move numbers (e.g. "1.", "12.", "1...", "1")
            if (MoveNumberRegex().IsMatch(token) || token.EndsWith('.'))
                continue;

            // Skip game results ("1-0", "0-1", "1/2-1/2", "*")
            if (token is "1-0" or "0-1" or "1/2-1/2" or "*")
                continue;

            moveNumber++;

            if (session.Status != GameStatus.InProgress)
            {
                throw new FormatException($"Move {moveNumber}: the game is already over.");
            }

            var move = MatchMove(session, token, moveNumber);
            bool applied = session.TryMakeMove(move);
            if (!applied)
            {
                throw new FormatException($"Move {moveNumber}: failed to apply move '{token}'.");
            }
        }

        return new GameRecord
        {
            Session = session,
            Tags = tags
        };
    }

    private static Move MatchMove(GameSession session, string token, int moveNumber)
    {
        var legalMoves = session.LegalMoves;

        // A. Exact notation match (e.g. "31-26", "26x17", "26x17x8")
        var exact = legalMoves.FirstOrDefault(m => string.Equals(m.Notation, token, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        // B. Normalized separator match (e.g. user wrote "26-17" or "26:17" for capture "26x17")
        string normToken = NormalizeSeparators(token);
        var normMatches = legalMoves.Where(m => NormalizeSeparators(m.Notation) == normToken).ToList();
        if (normMatches.Count == 1)
            return normMatches[0];

        // C. Start and end draughts square index match
        string[] parts = token.Split(['-', 'x', 'X', ':', '*'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out int fromIndex) &&
            int.TryParse(parts[^1], out int toIndex) &&
            Position.TryFromDraughtsIndex(fromIndex, out var fromPos) &&
            Position.TryFromDraughtsIndex(toIndex, out var toPos))
        {
            var matches = legalMoves.Where(m => m.From == fromPos && m.To == toPos).ToList();
            if (matches.Count == 1)
                return matches[0];

            if (matches.Count > 1 && parts.Length > 2)
            {
                // Disambiguate using intermediate route squares
                var pathIndices = parts.Select(int.Parse).ToList();
                var fullyMatched = matches.FirstOrDefault(m =>
                {
                    var mIndices = m.Path
                        .Select(p => p.ToDraughtsIndex())
                        .Where(idx => idx.HasValue)
                        .Select(idx => idx!.Value)
                        .ToList();
                    return mIndices.SequenceEqual(pathIndices);
                });

                if (fullyMatched != null)
                    return fullyMatched;
            }
        }

        throw new FormatException($"Move {moveNumber}: '{token}' is not a legal move for {session.CurrentState.ActivePlayer}.");
    }

    private static string NormalizeSeparators(string notation)
    {
        return notation
            .Replace('x', '-')
            .Replace('X', '-')
            .Replace(':', '-')
            .Replace('*', '-');
    }
}
