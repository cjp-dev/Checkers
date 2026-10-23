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
        "Checkers (Draughts) is an 8 × 8 board game played with 12 pieces per side with selectable rule variants — " +
        "International Draughts (Flying Kings) and English Checkers (1-step Kings). " +
        "You can play against the computer or against a friend, and watch the computer think live in the real-time analysis panel.",
        "Its tournament-grade brain searches ahead using 64-bit bitboards, an embedded 12-ply opening book, " +
        "Principal Variation Search (PVS) with Late Move Reductions, 4-way cache-line Transposition Tables, " +
        "Quiescence search, and dynamic chess clock management.",
        "Built purely in C# and .NET 10 with Clean Architecture and PDN game persistence. " +
        "The exact same engine runs natively on Windows (WPF) and in modern web browsers via Blazor WebAssembly AOT.",
    ];
}
