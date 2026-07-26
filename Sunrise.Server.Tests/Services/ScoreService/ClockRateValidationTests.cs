using osu.Shared;
using Sunrise.Server.Services.Helpers.Scores;
using Sunrise.Shared.Database.Models;

namespace Sunrise.Server.Tests.Services.ScoreService;

public class ClockRateValidationTests
{
    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(0, 1.2)]
    [InlineData(0, 1.5)]
    [InlineData(64, 1.5)]
    [InlineData(256, 0.75)]
    public void ValidClockRatesAreAccepted(int mods, double clockRate)
    {
        var score = new Score { Mods = (Mods)mods, ClockRate = clockRate };

        Assert.False(SubmitScoreHelper.HasInvalidClockRate(score));
    }

    [Theory]
    [InlineData(0, 0.49)]
    [InlineData(0, 2.01)]
    [InlineData(64, 1.2)]
    [InlineData(256, 1.2)]
    public void InvalidClockRatesAreRejected(int mods, double clockRate)
    {
        var score = new Score { Mods = (Mods)mods, ClockRate = clockRate };

        Assert.True(SubmitScoreHelper.HasInvalidClockRate(score));
    }
}
