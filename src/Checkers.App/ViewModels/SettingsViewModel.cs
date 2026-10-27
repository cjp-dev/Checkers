using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Checkers.App.Models;
using Checkers.Core.AI;
using Checkers.Core.Models;

namespace Checkers.App.ViewModels;

/// <summary>
/// ViewModel for the computer thinking, transposition table, search threads, and checkers variant settings dialog.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInternational), nameof(IsEnglish), nameof(OpeningBookDescription))]
    private CheckersVariant _variant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedDepth), nameof(IsTimePerMove), nameof(IsTimePerGame))]
    private TimeControlMode _mode;

    [ObservableProperty]
    private int _depth;

    [ObservableProperty]
    private int _secondsPerMove;

    [ObservableProperty]
    private int _minutesPerGame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TranspositionTableEntries), nameof(TranspositionTableEntriesText))]
    private int _transpositionTablePower;

    [ObservableProperty]
    private bool _useOpeningBook;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThreadCountText))]
    private int _threadCount;

    public SettingsViewModel(GameSettings settings, EngineThreadOptions? threadOptions = null)
    {
        ThreadOptions = threadOptions ?? EngineThreadOptions.Desktop;
        var normalized = settings.Normalize();
        Variant = normalized.Variant;
        Mode = normalized.Mode;
        Depth = normalized.Depth;
        SecondsPerMove = normalized.SecondsPerMove;
        MinutesPerGame = normalized.MinutesPerGame;
        TranspositionTableEntries = normalized.TranspositionTableEntries;
        UseOpeningBook = normalized.UseOpeningBook;
        ThreadCount = Math.Min(normalized.ThreadCount, ThreadOptions.MaxThreads);
    }

    public EngineThreadOptions ThreadOptions { get; }
    public int MaxThreadCount => ThreadOptions.MaxThreads;
    public bool ThreadsAvailable => ThreadOptions.Available;

    /// <summary>"Auto (N threads)" for 0, otherwise "N threads".</summary>
    public string ThreadCountText => FormatThreads(ThreadCount);

    /// <summary>The choices for a drop-down: Auto, then 1 to <see cref="MaxThreadCount"/>.</summary>
    public IReadOnlyList<(int Value, string Text)> ThreadChoices =>
        Enumerable.Range(0, MaxThreadCount + 1).Select(n => (n, FormatThreads(n))).ToList();

    private string FormatThreads(int count) => count == GameSettings.AutoThreads
        ? $"Auto ({ThreadOptions.AutoThreads} {(ThreadOptions.AutoThreads == 1 ? "thread" : "threads")})"
        : $"{count} {(count == 1 ? "thread" : "threads")}";

    public int MaxDepth => GameSettings.MaxDepth;
    public int MaxSecondsPerMove => GameSettings.MaxSecondsPerMove;
    public int MaxMinutesPerGame => GameSettings.MaxMinutesPerGame;
    public int MinTranspositionTablePower => GameSettings.MinTranspositionTablePower;
    public int MaxTranspositionTablePower => GameSettings.MaxTranspositionTablePower;

    public int TranspositionTableEntries
    {
        get => 1 << Math.Clamp(TranspositionTablePower, MinTranspositionTablePower, MaxTranspositionTablePower);
        set
        {
            uint clamped = (uint)Math.Clamp(value, GameSettings.DefaultTranspositionTableEntries, GameSettings.MaxTranspositionTableEntries);
            uint rounded = BitOperations.RoundUpToPowerOf2(clamped);
            int power = BitOperations.Log2(rounded);
            TranspositionTablePower = Math.Clamp(power, MinTranspositionTablePower, MaxTranspositionTablePower);
        }
    }

    public string TranspositionTableEntriesText =>
        $"{TranspositionTableEntries.ToString("N0", CultureInfo.InvariantCulture)} entries";

    public bool IsInternational
    {
        get => Variant == CheckersVariant.International;
        set { if (value) Variant = CheckersVariant.International; }
    }

    public bool IsEnglish
    {
        get => Variant == CheckersVariant.English;
        set { if (value) Variant = CheckersVariant.English; }
    }

    public string OpeningBookDescription => Variant switch
    {
        CheckersVariant.English => "Checkers Opening Book (32,369 positions)",
        _ => "Checkers Opening Book (26,367 positions)"
    };

    public bool IsFixedDepth
    {
        get => Mode == TimeControlMode.FixedDepth;
        set => SelectMode(value, TimeControlMode.FixedDepth);
    }

    public bool IsTimePerMove
    {
        get => Mode == TimeControlMode.TimePerMove;
        set => SelectMode(value, TimeControlMode.TimePerMove);
    }

    public bool IsTimePerGame
    {
        get => Mode == TimeControlMode.TimePerGame;
        set => SelectMode(value, TimeControlMode.TimePerGame);
    }

    public GameSettings ToSettings() =>
        new GameSettings(Mode, Depth, SecondsPerMove, MinutesPerGame, Variant, TranspositionTableEntries, UseOpeningBook, ThreadCount).Normalize();

    private void SelectMode(bool selected, TimeControlMode mode)
    {
        if (selected)
        {
            Mode = mode;
        }
    }
}
