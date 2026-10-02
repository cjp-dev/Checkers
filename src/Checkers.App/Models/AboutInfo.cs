namespace Checkers.App.Models;

/// <summary>
/// Text and metadata for the About dialog, shared between WPF desktop and Blazor WebAssembly.
/// </summary>
public static class AboutInfo
{
    public const string Title = "About Checkers";

    public const string Version = "Checkers (Draughts) Version 1.0";

    public const string PictureCaption = "Claus Pedersen – IT-architect with a passion for AI and board games (2026)";

    public static IReadOnlyList<string> Paragraphs { get; } =
    [
        "Checkers is an 8 × 8 board game played with 12 pieces per side under International Flying Kings rules. " +
        "You can play against the computer or against a friend, and watch the computer think live in the real-time analysis panel.",
        "Its brain searches ahead using Negamax Alpha-Beta pruning, 64-bit Zobrist Transposition Tables, Quiescence search, " +
        "Iterative Deepening, and dynamic chess clock time management. It evaluates pieces, king power, center control, and advanced advancement heuristics.",
        "Built purely in C# 13 and .NET 10 with Clean Architecture. The exact same engine runs natively on Windows (WPF) and " +
        "in modern web browsers via Blazor WebAssembly AOT.",
    ];
}
