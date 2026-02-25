using System.Net;
using System.Text.Json;
using FluentAssertions;
using GameServer.Controllers;
using GameServer.Data;
using GameServer.DTOs;
using GameServer.Models;
using GameServer.Tests.TestUtils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ConnectionInfo = GameServer.DTOs.ConnectionInfo;

namespace GameServer.Tests.Controllers;

public class GamesControllerTests
{
    private static GamesController CreateControllerWithHttp(ApplicationDbContext ctx, IPAddress? remoteIp = null)
    {
        var logger = new Mock<ILogger<GamesController>>();
        var controller = new GamesController(ctx, logger.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        if (remoteIp != null)
            controller.HttpContext.Connection.RemoteIpAddress = remoteIp;

        return controller;
    }

    [Fact]
    public async Task RegisterSession_WhenIpMissing_UsesRemoteIp_AndCreatesSession()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var controller = CreateControllerWithHttp(ctx, IPAddress.Parse("127.0.0.1"));

        var result = await controller.RegisterSession(new RegisterSessionRequest { Port = 7777, IpAddress = null, MaxPlayers = 10 });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RegisterSessionResponse>().Subject;

        response.SessionId.Should().BeGreaterThan(0);
        response.Key.Should().NotBeNullOrWhiteSpace();

        var session = ctx.Games.Single(s => s.Id == response.SessionId);
        session.MaxPlayers.Should().Be(10);
        session.State.Should().Be(GameState.Waiting);
        session.PlayerCount.Should().Be(0);

        var conn = JsonSerializer.Deserialize<ConnectionInfo>(session.ConnectionData)!;
        conn.Ip.Should().Be("127.0.0.1");
        conn.Port.Should().Be(7777);
        conn.Key.Should().Be(response.Key);
    }

    [Fact]
    public async Task RegisterSession_WhenRemoteIsIpv6Loopback_MapsTo127001()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var controller = CreateControllerWithHttp(ctx, IPAddress.Parse("::1"));

        var result = await controller.RegisterSession(new RegisterSessionRequest { Port = 7777, IpAddress = null, MaxPlayers = 20 });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (RegisterSessionResponse)ok.Value!;

        var session = ctx.Games.Single(s => s.Id == response.SessionId);
        var conn = JsonSerializer.Deserialize<ConnectionInfo>(session.ConnectionData)!;
        conn.Ip.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task JoinGame_WhenNoSessions_ReturnsNotFound()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var logger = new Mock<ILogger<GamesController>>();
        var controller = new GamesController(ctx, logger.Object);

        var result = await controller.JoinGame();

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task JoinGame_WhenSessionAvailable_ReturnsConnectionInfo()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        Seed.AddSession(ctx, GameState.Waiting, playerCount: 3, maxPlayers: 10, ip: "10.0.0.5", port: 8888, key: "abc");

        var logger = new Mock<ILogger<GamesController>>();
        var controller = new GamesController(ctx, logger.Object);

        var result = await controller.JoinGame();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var conn = ok.Value.Should().BeOfType<ConnectionInfo>().Subject;

        conn.Ip.Should().Be("10.0.0.5");
        conn.Port.Should().Be(8888);
        conn.Key.Should().Be("abc");
    }

    [Fact]
    public async Task JoinGame_WhenConnectionDataCorrupted_Returns500()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var session = new GameSession
        {
            State = GameState.Waiting,
            PlayerCount = 0,
            MaxPlayers = 10,
            ConnectionData = "NOT_JSON"
        };
        ctx.Games.Add(session);
        ctx.SaveChanges();

        var logger = new Mock<ILogger<GamesController>>();
        var controller = new GamesController(ctx, logger.Object);

        var result = await controller.JoinGame();

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task SubmitResult_WhenSessionNotFound_ReturnsNotFound()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var controller = CreateControllerWithHttp(ctx);

        var result = await controller.SubmitResult(new GameResultRequest
        {
            SessionId = 999,
            Leaderboard = new List<PlayerResultDto> { new() { PlayerId = 1, Kills = 1, Deaths = 0 } }
        });

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SubmitResult_WhenAlreadyFinished_ReturnsOk_AndDoesNotChangeStats()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var p = Seed.AddPlayer(ctx, "p1", "pass");
        Seed.AddLeaderboard(ctx, p, kills: 10, deaths: 5, gamesPlayed: 2);

        var session = Seed.AddSession(ctx, GameState.Finished, playerCount: 0, maxPlayers: 10);

        var controller = CreateControllerWithHttp(ctx);

        var result = await controller.SubmitResult(new GameResultRequest
        {
            SessionId = session.Id,
            Leaderboard = new List<PlayerResultDto> { new() { PlayerId = p.Id, Kills = 99, Deaths = 99 } }
        });

        result.Should().BeOfType<OkResult>();

        var stat = ctx.Leaderboard.Single(s => s.PlayerId == p.Id);
        stat.Kills.Should().Be(10);
        stat.Deaths.Should().Be(5);
        stat.GamesPlayed.Should().Be(2);
    }

    [Fact]
    public async Task SubmitResult_WhenValid_UpdatesStatsAndFinishesSession()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var p1 = Seed.AddPlayer(ctx, "p1", "pass");
        var p2 = Seed.AddPlayer(ctx, "p2", "pass");

        Seed.AddLeaderboard(ctx, p1, kills: 0, deaths: 0, gamesPlayed: 0);
        Seed.AddLeaderboard(ctx, p2, kills: 5, deaths: 1, gamesPlayed: 3);

        var session = Seed.AddSession(ctx, GameState.Playing, playerCount: 2, maxPlayers: 10);

        var controller = CreateControllerWithHttp(ctx);

        var result = await controller.SubmitResult(new GameResultRequest
        {
            SessionId = session.Id,
            Leaderboard = new List<PlayerResultDto>
            {
                new() { PlayerId = p1.Id, Kills = 2, Deaths = 1 },
                new() { PlayerId = p2.Id, Kills = 1, Deaths = 3 }
            }
        });

        result.Should().BeOfType<OkResult>();

        var s1 = ctx.Leaderboard.Single(s => s.PlayerId == p1.Id);
        s1.Kills.Should().Be(2);
        s1.Deaths.Should().Be(1);
        s1.GamesPlayed.Should().Be(1);

        var s2 = ctx.Leaderboard.Single(s => s.PlayerId == p2.Id);
        s2.Kills.Should().Be(6);
        s2.Deaths.Should().Be(4);
        s2.GamesPlayed.Should().Be(4);

        var updatedSession = ctx.Games.Single(g => g.Id == session.Id);
        updatedSession.State.Should().Be(GameState.Finished);
    }

    [Fact]
    public async Task PlayerJoined_WhenSessionDoesNotExist_CreatesItAndIncrementsCount()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var controller = CreateControllerWithHttp(ctx);

        var result = await controller.PlayerJoined(new PlayerJoinedRequest(SessionId: 123, PlayerId: 1));

        result.Should().BeOfType<OkResult>();

        var session = ctx.Games.Single(g => g.Id == 123);
        session.State.Should().Be(GameState.Waiting);
        session.PlayerCount.Should().Be(1);
    }

    [Fact]
    public async Task HealthCheck_WhenSessionNotFound_ReturnsNotFound()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var controller = CreateControllerWithHttp(ctx);

        var result = await controller.HealthCheck(new HealthCheckRequest(999, "Playing", "t", new List<int> { 1, 2 }));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task HealthCheck_WhenWaitingToPlaying_SetsStartTime_AndUpdatesCount()
    {
        using var factory = new SqliteDbContextFactory();
        using var ctx = factory.CreateContext();

        var session = Seed.AddSession(ctx, GameState.Waiting, playerCount: 0, maxPlayers: 10);
        session.StartTime = null;
        ctx.SaveChanges();

        var controller = CreateControllerWithHttp(ctx);

        var result = await controller.HealthCheck(new HealthCheckRequest(session.Id, "Playing", "t", new List<int> { 1, 2, 3 }));

        result.Should().BeOfType<OkResult>();

        var updated = ctx.Games.Single(g => g.Id == session.Id);
        updated.State.Should().Be(GameState.Playing);
        updated.PlayerCount.Should().Be(3);
        updated.StartTime.Should().NotBeNull();
        updated.LastHeartbeat.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }
}