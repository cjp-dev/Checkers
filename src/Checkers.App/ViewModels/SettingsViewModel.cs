using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Checkers.App.Models;
using Checkers.Core.AI;
using Checkers.Core.Models;

namespace Checkers.App.ViewModels;

/// <summary>
/// ViewModel for the computer thinking, transposition table, and checkers variant settings dialog.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInternational), nameof(IsEnglish))]
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

    public SettingsViewModel(GameSettings settings)
    {
        var normalized = settings.Normalize();
        Variant = normalized.Variant;
        Mode = normalized.Mode;
        Depth = normalized.Depth;
        SecondsPerMove = normalized.SecondsPerMove;
        MinutesPerGame = normalized.MinutesPerGame;
        TranspositionTableEntries = normalized.TranspositionTableEntries;
    }

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
        new GameSettings(Mode, Depth, SecondsPerMove, MinutesPerGame, Variant, TranspositionTableEntries).Normalize();

    private void SelectMode(bool selected, TimeControlMode mode)
    {
        if (selected)
        {
            Mode = mode;
        }
    }
}
