using System.Globalization;

namespace Checkers.Core.AI.Book;

/// <summary>
/// A candidate move stored on an opening book position along with its backed-up centipawn score
/// from the active player's (<c>SideToMove</c>) perspective.
/// </summary>
public readonly record struct BookMoveEntry(string Notation, int Score);

/// <summary>
/// A unique board position in the opening book, keyed by its 64-bit <see cref="ZobristHash"/>.
/// Internal positions (<c>Ply &lt; Depth</c>) store their top 2–3 moves in <see cref="Moves"/> (sorted best-first);
/// leaf positions (<c>Ply == Depth</c>) have an empty <see cref="Moves"/> list.
/// </summary>
public sealed record BookNode(
    ulong ZobristHash,
    int Ply,
    int Score,
    IReadOnlyList<BookMoveEntry> Moves);

/// <summary>
/// Reads and writes the human-readable Checkers opening book text format and <c>.partial</c> leaf checkpoints.
/// Format per line: <c>&lt;ZobristHex16&gt; &lt;Ply&gt; &lt;Score&gt; [Move1:Score1 Move2:Score2 ...]</c>.
/// Lines starting with <c>#</c> and blank lines are ignored.
/// </summary>
public static class BookFile
{
    public const int MinScore = -30_000;
    public const int MaxScore = 30_000;

    public static List<BookNode> Read(TextReader reader)
    {
        var nodes = new List<BookNode>();
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            nodes.Add(Parse(trimmed, lineNumber));
        }

        return nodes;
    }

    public static BookNode Parse(string text, int lineNumber = 0)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !ulong.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hash)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ply)
            || ply < 0
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int score)
            || score is < MinScore or > MaxScore)
        {
            throw new FormatException(
                $"Line {lineNumber}: expected \"<ZobristHex> <Ply> <Score> [Move:Score ...]\", found \"{text}\".");
        }

        if (parts.Length == 3)
        {
            return new BookNode(hash, ply, score, []);
        }

        var moves = new List<BookMoveEntry>(parts.Length - 3);
        for (int i = 3; i < parts.Length; i++)
        {
            int colonIndex = parts[i].LastIndexOf(':');
            if (colonIndex <= 0
                || colonIndex == parts[i].Length - 1
                || !int.TryParse(parts[i].AsSpan(colonIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int moveScore)
                || moveScore is < MinScore or > MaxScore)
            {
                throw new FormatException(
                    $"Line {lineNumber}: invalid move token \"{parts[i]}\" in \"{text}\". Expected \"<Notation>:<Score>\".");
            }

            string notation = parts[i][..colonIndex];
            moves.Add(new BookMoveEntry(notation, moveScore));
        }

        return new BookNode(hash, ply, score, moves);
    }

    public static void Write(TextWriter writer, IEnumerable<BookNode> nodes, string? comment = null)
    {
        if (!string.IsNullOrEmpty(comment))
        {
            foreach (string rawLine in comment.Replace("\r\n", "\n").Split('\n'))
            {
                writer.Write("# ");
                writer.Write(rawLine);
                writer.Write('\n');
            }
        }

        foreach (BookNode node in nodes)
        {
            writer.Write(Format(node));
            writer.Write('\n');
        }
    }

    public static string Format(BookNode node)
    {
        string scoreStr = FormatSignedScore(node.Score);
        if (node.Moves.Count == 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{node.ZobristHash:X16} {node.Ply} {scoreStr}");
        }

        string movesPart = string.Join(
            ' ',
            node.Moves.Select(m => $"{m.Notation}:{FormatSignedScore(m.Score)}"));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{node.ZobristHash:X16} {node.Ply} {scoreStr} {movesPart}");
    }

    public static string FormatSignedScore(int score) =>
        score > 0
            ? "+" + score.ToString(CultureInfo.InvariantCulture)
            : score.ToString(CultureInfo.InvariantCulture);

    public static string FormatPartialLeaf(ulong hash, int score) =>
        string.Create(CultureInfo.InvariantCulture, $"{hash:X16} {FormatSignedScore(score)}");

    public static bool TryParsePartialLeaf(string line, out ulong hash, out int score)
    {
        hash = 0;
        score = 0;
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return false;
        }

        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && ulong.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash)
            && int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out score)
            && score is >= MinScore and <= MaxScore;
    }

    public static bool TryParseNode(string line, out BookNode? node)
    {
        node = null;
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return false;
        }

        try
        {
            node = Parse(trimmed);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static Dictionary<ulong, BookNode> ReadPartialNodes(TextReader reader)
    {
        var result = new Dictionary<ulong, BookNode>();
        while (reader.ReadLine() is { } line)
        {
            if (TryParseNode(line, out var node) && node != null)
            {
                result[node.ZobristHash] = node;
            }
        }

        return result;
    }
}

