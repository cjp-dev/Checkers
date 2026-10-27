namespace Checkers.App.Models;

/// <summary>
/// What the Threads setting can offer on this host: the thread count Auto stands for, the largest manual
/// choice, and whether the engine can use more than one thread at all (not in a browser page that
/// isn't cross-origin isolated).
/// </summary>
public sealed record EngineThreadOptions(int AutoThreads, int MaxThreads, bool Available = true)
{
    /// <summary>Desktop: Auto is one thread per logical processor (at most 16); up to all logical processors.</summary>
    public static EngineThreadOptions Desktop { get; } =
        new(Math.Min(16, Environment.ProcessorCount), Math.Max(1, Math.Min(GameSettings.MaxThreadCount, Environment.ProcessorCount)));
}
