using System.Net;
using Sunrise.API.Serializable.Response;
using Sunrise.Shared.Enums.Beatmaps;
using Sunrise.Shared.Enums.Users;
using Sunrise.Tests.Abstracts;
using Sunrise.Tests.Extensions;
using Sunrise.Tests.Services.Mock;
using Sunrise.Tests.Utils;
using Sunrise.Tests;

namespace Sunrise.Server.Tests.API.UserController;

[Collection("Integration tests collection")]
public class ApiUserLeaderboardTests(IntegrationDatabaseFixture fixture) : ApiTest(fixture)
{
    private readonly MockService _mocker = new();

    public static IEnumerable<object[]> GetGameModes()
    {
        return Enum.GetValues(typeof(GameMode)).Cast<GameMode>().Select(mode => new object[]
        {
            mode
        });
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100")]
    [InlineData("test")]
    public async Task TestLeaderboardInvalidLeaderboardType(string leaderboardType)
    {
        // Arrange
        var client = App.CreateClient().UseClient("api");

        // Act
        var response = await client.GetAsync($"user/leaderboard?type={leaderboardType}");

        // Assert
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("test")]
    public async Task TestLeaderboardInvalidLimit(string limit)
    {
        // Arrange
        var client = App.CreateClient().UseClient("api");

        // Act
        var response = await client.GetAsync($"user/leaderboard?limit={limit}");

        // Assert
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("test")]
    public async Task TestLeaderboardUserInvalidPage(string page)
    {
        // Arrange
        var client = App.CreateClient().UseClient("api");

        // Act
        var response = await client.GetAsync($"user/leaderboard?page={page}");

        // Assert
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(GetGameModes))]
    public async Task TestLeaderboard(GameMode gamemode)
    {
        // Arrange
        var client = App.CreateClient().UseClient("api");

        var usersNumber = _mocker.GetRandomInteger(minInt: 2, maxInt: 5);

        var userIdsSortedByPp = new List<int>();

        for (var i = 0; i < usersNumber; i++)
        {
            var user = _mocker.User.GetRandomUser();
            user = await CreateTestUser(user);

            userIdsSortedByPp.Add(user.Id);

            var stats = await Database.Users.Stats.GetUserStats(user.Id, gamemode);
            if (stats == null)
                throw new Exception("User stats not found");

            stats.PerformancePoints = i * 100;

            await Database.Users.Stats.UpdateUserStats(stats, user);
        }

        // Act
        var response = await client.GetAsync($"user/leaderboard?mode={(int)gamemode}");

        // Assert
        response.EnsureSuccessStatusCode();

        var responseData = await response.Content.ReadFromJsonAsyncWithAppConfig<LeaderboardResponse>();
        Assert.NotNull(responseData);

        Assert.Equivalent(userIdsSortedByPp.LastOrDefault(), responseData.Users.FirstOrDefault()?.User.Id);
    }

    [Fact]
    public async Task TestLeaderboardWithoutRestrictedUsers()
    {
        // Arrange
        var client = App.CreateClient().UseClient("api");

        var usersNumber = _mocker.GetRandomInteger(minInt: 2, maxInt: 5);

        for (var i = 0; i < usersNumber; i++)
        {
            await CreateTestUser();
        }

        var restrictedUser = await CreateTestUser();

        await Database.Users.Moderation.RestrictPlayer(restrictedUser.Id, null, "Test");

        // Act
        var response = await client.GetAsync("user/leaderboard?mode=0&limit=10");

        // Assert
        response.EnsureSuccessStatusCode();

        var responseData = await response.Content.ReadFromJsonAsyncWithAppConfig<LeaderboardResponse>();
        Assert.NotNull(responseData);

        Assert.DoesNotContain(responseData.Users, x => x.User.Id == restrictedUser.Id);
    }

    [Fact]
    public async Task TestLeaderboardLimitAndPage()
    {
        // Arrange
        var client = App.CreateClient().UseClient("api");

        var usersNumber = _mocker.GetRandomInteger(minInt: 3, maxInt: 5);

        var gamemode = _mocker.Score.GetRandomGameMode();

        var userIdsSortedByPp = new List<int>();

        for (var i = 0; i < usersNumber; i++)
        {
            var user = _mocker.User.GetRandomUser();
            user = await CreateTestUser(user);

            userIdsSortedByPp.Add(user.Id);

            var stats = await Database.Users.Stats.GetUserStats(user.Id, gamemode);
            if (stats == null)
                throw new Exception("User stats not found");

            stats.PerformancePoints = i * 100;

            await Database.Users.Stats.UpdateUserStats(stats, user);
        }

        // Act
        var response = await client.GetAsync($"user/leaderboard?mode={(int)gamemode}&limit=1&page=2");

        // Assert
        response.EnsureSuccessStatusCode();

        var responseData = await response.Content.ReadFromJsonAsyncWithAppConfig<LeaderboardResponse>();
        Assert.NotNull(responseData);

        Assert.Single(responseData.Users);
        Assert.Equivalent(userIdsSortedByPp.SkipLast(1).LastOrDefault(), responseData.Users.FirstOrDefault()?.User.Id);
    }

    [Fact]
    public async Task TestLeaderboardFiltersByCountry()
    {
        // Arrange
        var client = App.CreateClient().UseClient("api");
        var gamemode = _mocker.Score.GetRandomGameMode();

        var usTopUser = _mocker.User.GetRandomUser();
        usTopUser.Country = CountryCode.US;
        usTopUser = await CreateTestUser(usTopUser);

        var usLowerUser = _mocker.User.GetRandomUser();
        usLowerUser.Country = CountryCode.US;
        usLowerUser = await CreateTestUser(usLowerUser);

        var jpHigherUser = _mocker.User.GetRandomUser();
        jpHigherUser.Country = CountryCode.JP;
        jpHigherUser = await CreateTestUser(jpHigherUser);

        var usTopStats = await Database.Users.Stats.GetUserStats(usTopUser.Id, gamemode);
        var usLowerStats = await Database.Users.Stats.GetUserStats(usLowerUser.Id, gamemode);
        var jpHigherStats = await Database.Users.Stats.GetUserStats(jpHigherUser.Id, gamemode);

        if (usTopStats == null || usLowerStats == null || jpHigherStats == null)
            throw new Exception("User stats not found");

        usTopStats.PerformancePoints = 200;
        usLowerStats.PerformancePoints = 100;
        jpHigherStats.PerformancePoints = 300;

        await Database.Users.Stats.UpdateUserStats(usTopStats, usTopUser);
        await Database.Users.Stats.UpdateUserStats(usLowerStats, usLowerUser);
        await Database.Users.Stats.UpdateUserStats(jpHigherStats, jpHigherUser);

        // Act
        var response = await client.GetAsync($"user/leaderboard?mode={(int)gamemode}&type=Pp&page=1&limit=10&country=US");

        // Assert
        response.EnsureSuccessStatusCode();

        var responseData = await response.Content.ReadFromJsonAsyncWithAppConfig<LeaderboardResponse>();
        Assert.NotNull(responseData);

        Assert.Equal(2, responseData.TotalCount);
        Assert.Equal([usTopUser.Id, usLowerUser.Id], responseData.Users.Select(x => x.User.Id));
        Assert.All(responseData.Users, x => Assert.Equal(CountryCode.US, x.User.Country));
    }
}
