using Checkers.App.Models;
using Checkers.App.ViewModels;
using Checkers.Core.AI;
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
    }
}
