using Checkers.App.Models;
using Checkers.App.ViewModels;
using Checkers.Core.AI;
using Checkers.Core.Models;
using FluentAssertions;

namespace Checkers.App.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void Constructor_InitializesValuesFromSettings()
    {
        var settings = new GameSettings(TimeControlMode.TimePerMove, 10, 15, 20);
        var vm = new SettingsViewModel(settings);

        vm.Mode.Should().Be(TimeControlMode.TimePerMove);
        vm.Depth.Should().Be(10);
        vm.SecondsPerMove.Should().Be(15);
        vm.MinutesPerGame.Should().Be(20);
        vm.IsTimePerMove.Should().BeTrue();
        vm.IsFixedDepth.Should().BeFalse();
        vm.IsTimePerGame.Should().BeFalse();
    }

    [Fact]
    public void RadioProperties_UpdateModeCorrectly()
    {
        var vm = new SettingsViewModel(GameSettings.Default);

        vm.IsFixedDepth = true;
        vm.Mode.Should().Be(TimeControlMode.FixedDepth);
        vm.IsFixedDepth.Should().BeTrue();
        vm.IsTimePerMove.Should().BeFalse();
        vm.IsTimePerGame.Should().BeFalse();

        vm.IsTimePerMove = true;
        vm.Mode.Should().Be(TimeControlMode.TimePerMove);
        vm.IsTimePerMove.Should().BeTrue();
        vm.IsFixedDepth.Should().BeFalse();
        vm.IsTimePerGame.Should().BeFalse();

        vm.IsTimePerGame = true;
        vm.Mode.Should().Be(TimeControlMode.TimePerGame);
        vm.IsTimePerGame.Should().BeTrue();
        vm.IsFixedDepth.Should().BeFalse();
        vm.IsTimePerMove.Should().BeFalse();
    }

    [Fact]
    public void ToSettings_ClampsValuesToBounds()
    {
        var vm = new SettingsViewModel(new GameSettings(TimeControlMode.FixedDepth, 999, -5, 120));
        var result = vm.ToSettings();

        result.Depth.Should().Be(GameSettings.MaxDepth);
        result.SecondsPerMove.Should().Be(1);
        result.MinutesPerGame.Should().Be(GameSettings.MaxMinutesPerGame);
    }

    [Theory]
    [InlineData(TimeControlMode.FixedDepth, 8, 5, 5, "8 plies")]
    [InlineData(TimeControlMode.TimePerMove, 8, 12, 5, "12s / move")]
    [InlineData(TimeControlMode.TimePerGame, 8, 5, 10, "10 min / game")]
    public void GameSettings_Summary_MatchesExpectedFormat(
        TimeControlMode mode, int depth, int seconds, int minutes, string expected)
    {
        var settings = new GameSettings(mode, depth, seconds, minutes);
        settings.Summary.Should().Be(expected);
    }

    [Fact]
    public void GameSettings_ToLimits_CreatesCorrectLimits()
    {
        var fixedDepth = new GameSettings(TimeControlMode.FixedDepth, 6, 5, 5);
        var fdLimits = fixedDepth.ToLimits(TimeSpan.FromMinutes(5));
        fdLimits.Mode.Should().Be(TimeControlMode.FixedDepth);
        fdLimits.Depth.Should().Be(6);

        var timePerMove = new GameSettings(TimeControlMode.TimePerMove, 8, 7, 5);
        var tpmLimits = timePerMove.ToLimits(TimeSpan.FromMinutes(5));
        tpmLimits.Mode.Should().Be(TimeControlMode.TimePerMove);
        tpmLimits.Time.Should().Be(TimeSpan.FromSeconds(7));

        var timePerGame = new GameSettings(TimeControlMode.TimePerGame, 8, 5, 15);
        var tpgLimits = timePerGame.ToLimits(TimeSpan.FromMinutes(12));
        tpgLimits.Mode.Should().Be(TimeControlMode.TimePerGame);
        tpgLimits.Time.Should().Be(TimeSpan.FromMinutes(12));
        tpgLimits.UseOpeningBook.Should().BeTrue();

        var bookDisabled = new GameSettings(TimeControlMode.TimePerMove, 8, 5, 5, UseOpeningBook: false);
        bookDisabled.ToLimits(TimeSpan.FromMinutes(5)).UseOpeningBook.Should().BeFalse();
    }

    [Fact]
    public void OpeningBookDescription_UpdatesDynamicallyWithVariant()
    {
        var vm = new SettingsViewModel(GameSettings.Default);
        vm.Variant.Should().Be(CheckersVariant.International);
        vm.OpeningBookDescription.Should().Be("Checkers Opening Book (26,367 positions)");

        string? notifiedProperty = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.OpeningBookDescription))
            {
                notifiedProperty = e.PropertyName;
            }
        };

        vm.IsEnglish = true;
        vm.OpeningBookDescription.Should().Be("Checkers Opening Book (32,369 positions)");
        notifiedProperty.Should().Be(nameof(SettingsViewModel.OpeningBookDescription));

        vm.IsInternational = true;
        vm.OpeningBookDescription.Should().Be("Checkers Opening Book (26,367 positions)");
    }

    [Fact]
    public void UseOpeningBook_TogglesCorrectly_AndPersistsInToSettings()
    {
        var vm = new SettingsViewModel(GameSettings.Default);
        vm.UseOpeningBook.Should().BeTrue();

        vm.UseOpeningBook = false;
        vm.ToSettings().UseOpeningBook.Should().BeFalse();

        vm.UseOpeningBook = true;
        vm.ToSettings().UseOpeningBook.Should().BeTrue();
    }

    [Fact]
    public void ThreadCount_DefaultsToAuto_AndPersistsInToSettings()
    {
        var vm = new SettingsViewModel(GameSettings.Default, new EngineThreadOptions(AutoThreads: 6, MaxThreads: 12));
        vm.ThreadCount.Should().Be(GameSettings.AutoThreads);
        vm.ThreadCountText.Should().Be("Auto (6 threads)");
        vm.MaxThreadCount.Should().Be(12);

        vm.ThreadCount = 1;
        vm.ThreadCountText.Should().Be("1 thread");
        vm.ToSettings().ThreadCount.Should().Be(1);

        vm.ThreadCount = 8;
        vm.ThreadCountText.Should().Be("8 threads");
        vm.ToSettings().ThreadCount.Should().Be(8);
    }

    [Fact]
    public void ThreadCount_AboveTheHostMaximum_IsClamped()
    {
        var vm = new SettingsViewModel(GameSettings.Default with { ThreadCount = 16 }, new EngineThreadOptions(4, 8, Available: false));

        vm.ThreadCount.Should().Be(8);
        vm.ThreadsAvailable.Should().BeFalse();
        vm.ThreadChoices.Select(c => c.Value).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8);
        vm.ThreadChoices[0].Text.Should().Be("Auto (4 threads)");
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(1000, MinimaxPlayer.MaxThreads)]
    public void GameSettings_Normalize_ClampsThreadCount(int threadCount, int expected)
    {
        (GameSettings.Default with { ThreadCount = threadCount }).Normalize().ThreadCount.Should().Be(expected);
    }

    [Fact]
    public void GameSettings_ResolveThreads_UsesAutoOnlyForZero()
    {
        GameSettings.Default.ResolveThreads(autoThreads: 6).Should().Be(6);
        (GameSettings.Default with { ThreadCount = 3 }).ResolveThreads(autoThreads: 6).Should().Be(3);
    }
}
