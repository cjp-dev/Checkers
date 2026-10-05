using System.Diagnostics.CodeAnalysis;
using Checkers.Core.Models;

namespace Checkers.Core.AI.Book;

/// <summary>
/// Transposition-aware Checkers opening book keyed by 64-bit <see cref="BoardState.ZobristHash"/>.
/// Supports instant $O(1)$ position lookup and randomized selection among top moves within a centipawn margin.
/// </summary>
public sealed class OpeningBook
{
    public const int DefaultRandomMarginCp = 10;

    private const string EnglishResourceName = "Checkers.Core.AI.Book.OpeningBook.English.txt";
    private const string InternationalResourceName = "Checkers.Core.AI.Book.OpeningBook.International.txt";

    private static readonly Lazy<OpeningBook> s_defaultEnglish = new(() => LoadEmbeddedResource(EnglishResourceName));
    private static readonly Lazy<OpeningBook> s_defaultInternational = new(() => LoadEmbeddedResource(InternationalResourceName));

    private readonly Dictionary<ulong, BookNode> _nodesByHash;

    public static OpeningBook Empty { get; } = new([], -1);

    /// <summary>The built-in English Checkers opening book, loaded lazily on first use.</summary>
    public static OpeningBook DefaultEnglish => s_defaultEnglish.Value;

    /// <summary>The built-in International (Flying Kings) Checkers opening book, loaded lazily on first use.</summary>
    public static OpeningBook DefaultInternational => s_defaultInternational.Value;

    /// <summary>Returns the built-in opening book for the specified <paramref name="variant"/>.</summary>
    public static OpeningBook GetDefault(CheckersVariant variant) =>
        variant == CheckersVariant.English ? DefaultEnglish : DefaultInternational;

    /// <summary>Maximum ply depth stored in the book, or -1 if the book is empty.</summary>
    public int Depth { get; }

    /// <summary>Total number of unique positions (internal + leaf nodes) stored in the book.</summary>
    public int Count => _nodesByHash.Count;

    /// <summary>Number of internal positions in the book that have at least one stored book move.</summary>
    public int MovePositionsCount { get; }

    public IReadOnlyCollection<BookNode> Nodes => _nodesByHash.Values;

    private OpeningBook(Dictionary<ulong, BookNode> nodesByHash, int depth)
    {
        _nodesByHash = nodesByHash;
        Depth = depth;
        MovePositionsCount = nodesByHash.Values.Count(n => n.Moves.Count > 0);
    }

    public static OpeningBook Load(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return FromNodes(BookFile.Read(reader));
    }

    public static OpeningBook Load(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Load(stream);
    }

    public static OpeningBook FromNodes(IEnumerable<BookNode> nodes)
    {
        var map = new Dictionary<ulong, BookNode>();
        int maxDepth = -1;

        foreach (BookNode node in nodes)
        {
            if (map.TryGetValue(node.ZobristHash, out var existing))
            {
                if (existing.Score != node.Score)
                {
                    throw new FormatException(
                        $"The opening book contains conflicting scores ({existing.Score} vs {node.Score}) for Zobrist hash {node.ZobristHash:X16}.");
                }

                // Prefer the entry that carries the expanded moves if one is an internal node
                if (existing.Moves.Count == 0 && node.Moves.Count > 0)
                {
                    map[node.ZobristHash] = node;
                }
                continue;
            }

            map[node.ZobristHash] = node;
            maxDepth = Math.Max(maxDepth, node.Ply);
        }

        return new OpeningBook(map, maxDepth);
    }

    public bool TryGetNode(ulong zobristHash, [NotNullWhen(true)] out BookNode? node) =>
        _nodesByHash.TryGetValue(zobristHash, out node);

    public bool TryGetScore(BoardState state, out int score)
    {
        if (_nodesByHash.TryGetValue(state.ZobristHash, out var node))
        {
            score = node.Score;
            return true;
        }

        score = 0;
        return false;
    }

    /// <summary>
    /// Looks up <paramref name="state"/> by its 64-bit <see cref="BoardState.ZobristHash"/> and selects a move
    /// at random among all stored book moves within <paramref name="marginCp"/> centipawns of the best stored move.
    /// </summary>
    public bool TryGetMove(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        Random random,
        int marginCp,
        [NotNullWhen(true)] out Move? move,
        out int score)
    {
        move = null;
        score = 0;

        if (legalMoves.Count == 0 || !_nodesByHash.TryGetValue(state.ZobristHash, out var node) || node.Moves.Count == 0)
        {
            return false;
        }

        var matchedCandidates = new List<(Move LegalMove, BookMoveEntry BookEntry)>(node.Moves.Count);
        foreach (var entry in node.Moves)
        {
            var legal = legalMoves.FirstOrDefault(
                m => string.Equals(m.Notation, entry.Notation, StringComparison.OrdinalIgnoreCase));
            if (legal != null)
            {
                matchedCandidates.Add((legal, entry));
            }
        }

        if (matchedCandidates.Count == 0)
        {
            return false;
        }

        int bestScore = matchedCandidates.Max(c => c.BookEntry.Score);
        int threshold = bestScore - Math.Max(0, marginCp);
        var eligible = matchedCandidates
            .Where(c => c.BookEntry.Score >= threshold)
            .ToList();

        var chosen = eligible[random.Next(eligible.Count)];
        move = chosen.LegalMove;
        score = chosen.BookEntry.Score;
        return true;
    }

    private static OpeningBook LoadEmbeddedResource(string resourceName)
    {
        using Stream? stream = typeof(OpeningBook).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return Empty;
        }

        return Load(stream);
    }
}
