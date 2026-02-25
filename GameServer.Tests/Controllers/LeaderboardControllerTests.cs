using FluentAssertions;
using GameServer.Controllers;
using GameServer.Tests.TestUtils;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameServer.Tests.Controllers;

public class LeaderboardControllerTests
{
    [Fact]
    public async Task GetLeaderboard_WhenPlayerIdMissing_ReturnsBadRequest()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var controller = new LeaderboardController(ctx);

        var result = await controller.GetLeaderboard(limit: 10, playerId: 0);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetLeaderboard_WhenValid_ReturnsTopPlayersAndTotal()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var p1 = Seed.AddPlayer(ctx, "a", "pass");
        var p2 = Seed.AddPlayer(ctx, "b", "pass");
        var p3 = Seed.AddPlayer(ctx, "c", "pass");

        Seed.AddLeaderboard(ctx, p1, kills: 10, deaths: 1, gamesPlayed: 2);
        Seed.AddLeaderboard(ctx, p2, kills: 50, deaths: 5, gamesPlayed: 10);
        Seed.AddLeaderboard(ctx, p3, kills: 20, deaths: 2, gamesPlayed: 3);

        var controller = new LeaderboardController(ctx);

        var result = await controller.GetLeaderboard(limit: 2, playerId: p1.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value!;

        var leaderboard = (System.Collections.IEnumerable)payload.GetType().GetProperty("leaderboard")!.GetValue(payload)!;
        var totalPlayers = (int)payload.GetType().GetProperty("totalPlayers")!.GetValue(payload)!;

        totalPlayers.Should().Be(3);

        var list = leaderboard.Cast<object>().ToList();
        list.Should().HaveCount(2);

        list[0].GetType().GetProperty("nickname")!.GetValue(list[0])!.Should().Be("b");
        list[1].GetType().GetProperty("nickname")!.GetValue(list[1])!.Should().Be("c");
    }

    [Fact]
    public async Task GetPlayerStat_WhenNotFound_ReturnsNotFound()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var controller = new LeaderboardController(ctx);

        var result = await controller.GetPlayerStat(player_id: 999);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetPlayerStat_WhenFound_ReturnsOk()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var p1 = Seed.AddPlayer(ctx, "egor", "pass");
        Seed.AddLeaderboard(ctx, p1, kills: 7, deaths: 3, gamesPlayed: 2);

        var controller = new LeaderboardController(ctx);

        var result = await controller.GetPlayerStat(player_id: p1.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value!;

        payload.GetType().GetProperty("nickname")!.GetValue(payload).Should().Be("egor");
        payload.GetType().GetProperty("kills")!.GetValue(payload).Should().Be(7);
        payload.GetType().GetProperty("deaths")!.GetValue(payload).Should().Be(3);
        payload.GetType().GetProperty("gamesPlayed")!.GetValue(payload).Should().Be(2);
    }
}