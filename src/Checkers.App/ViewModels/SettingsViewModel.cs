using CommunityToolkit.Mvvm.ComponentModel;
using Checkers.App.Models;
using Checkers.Core.AI;
using Checkers.Core.Models;

namespace Checkers.App.ViewModels;

/// <summary>
/// ViewModel for the computer thinking and checkers variant settings dialog.
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

    public SettingsViewModel(GameSettings settings)
    {
        Variant = settings.Variant;
        Mode = settings.Mode;
        Depth = settings.Depth;
        SecondsPerMove = settings.SecondsPerMove;
        MinutesPerGame = settings.MinutesPerGame;
    }

    public int MaxDepth => GameSettings.MaxDepth;
    public int MaxSecondsPerMove => GameSettings.MaxSecondsPerMove;
    public int MaxMinutesPerGame => GameSettings.MaxMinutesPerGame;

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

    public GameSettings ToSettings() => new GameSettings(Mode, Depth, SecondsPerMove, MinutesPerGame, Variant).Normalize();

    private void SelectMode(bool selected, TimeControlMode mode)
    {
        if (selected)
        {
            Mode = mode;
        }
    }
}
