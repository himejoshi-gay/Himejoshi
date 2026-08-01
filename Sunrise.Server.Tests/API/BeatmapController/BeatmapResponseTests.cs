using Sunrise.API.Serializable.Response;
using Sunrise.Shared.Enums.Beatmaps;
using Sunrise.Tests.Services.Mock;

namespace Sunrise.Server.Tests.API.BeatmapController;

public class BeatmapResponseTests
{
    private readonly MockService _mocker = new();

    [Fact]
    public void MissingConvertedBeatmapsDoesNotPreventStandardBeatmapResponse()
    {
        var beatmapSet = _mocker.Beatmap.GetRandomBeatmapSet();
        var beatmap = beatmapSet.Beatmaps[0];
        beatmap.ModeInt = (int)GameMode.Standard;
        beatmapSet.ConvertedBeatmaps = null!;

        var response = new BeatmapResponse(null!, beatmap, beatmapSet);

        Assert.Equal(beatmap.DifficultyRating, response.StarRating);
        Assert.Equal(0, response.StarRatingTaiko);
        Assert.Equal(0, response.StarRatingCatch);
        Assert.Equal(0, response.StarRatingMania);
    }

    [Theory]
    [InlineData(GameMode.Taiko)]
    [InlineData(GameMode.CatchTheBeat)]
    [InlineData(GameMode.Mania)]
    public void ConvertedBeatmapRatingUsesMatchingMode(GameMode convertedMode)
    {
        var beatmapSet = _mocker.Beatmap.GetRandomBeatmapSet();
        var beatmap = beatmapSet.Beatmaps[0];
        beatmap.ModeInt = (int)GameMode.Standard;

        var convertedBeatmap = _mocker.Beatmap.GetRandomBeatmap(beatmapSet, true);
        convertedBeatmap.Id = beatmap.Id;
        convertedBeatmap.ModeInt = (int)convertedMode;
        convertedBeatmap.DifficultyRating = 9.5;
        beatmapSet.ConvertedBeatmaps = [convertedBeatmap];

        var response = new BeatmapResponse(null!, beatmap, beatmapSet);

        Assert.Equal(convertedMode == GameMode.Taiko ? 9.5 : 0, response.StarRatingTaiko);
        Assert.Equal(convertedMode == GameMode.CatchTheBeat ? 9.5 : 0, response.StarRatingCatch);
        Assert.Equal(convertedMode == GameMode.Mania ? 9.5 : 0, response.StarRatingMania);
    }
}
