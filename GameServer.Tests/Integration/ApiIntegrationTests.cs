using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameServer.DTOs;
using GameServer.Models;
using GameServer.Tests.TestUtils;

namespace GameServer.Tests.Integration;

public class ApiIntegrationTests
{
    [Fact(DisplayName = "Integration: match lifecycle updates leaderboard and player stats")]
    public async Task MatchLifecycleEndpoints_WhenCompleted_UpdateLeaderboardAndPlayerStat()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        var alpha = await ApiTestHelpers.RegisterUserAsync(client, "alpha_lifecycle", "secret123");
        var bravo = await ApiTestHelpers.RegisterUserAsync(client, "bravo_lifecycle", "secret123");

        await ApiTestHelpers.AuthorizeAsync(client, alpha.Token);

        var registerResponse = await client.PostAsJsonAsync("/games/register", new RegisterSessionRequest
        {
            Port = 7001,
            IpAddress = "10.0.0.10",
            MaxPlayers = 4
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await ApiTestHelpers.ReadAsAsync<RegisterSessionResponse>(registerResponse);

        session.SessionId.Should().BeGreaterThan(0);
        session.Key.Should().NotBeNullOrWhiteSpace();

        (await client.PostAsJsonAsync("/games/player_joined", new PlayerJoinedRequest(session.SessionId, alpha.PlayerId)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/games/player_joined", new PlayerJoinedRequest(session.SessionId, bravo.PlayerId)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var healthcheckResponse = await client.PostAsJsonAsync("/games/healthcheck", new HealthCheckRequest(
            session.SessionId,
            "Playing",
            "12:00:00",
            [alpha.PlayerId, bravo.PlayerId]));

        healthcheckResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultResponse = await client.PostAsJsonAsync("/games/result", new GameResultRequest
        {
            SessionId = session.SessionId,
            Leaderboard =
            [
                new PlayerResultDto { PlayerId = alpha.PlayerId, Kills = 5, Deaths = 2 },
                new PlayerResultDto { PlayerId = bravo.PlayerId, Kills = 2, Deaths = 5 }
            ]
        });

        resultResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var leaderboardResponse = await client.GetAsync($"/leaderboard?players_limit=10&player_id={alpha.PlayerId}");
        leaderboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var leaderboard = await ApiTestHelpers.ReadAsAsync<LeaderboardEnvelope>(leaderboardResponse);
        leaderboard.TotalPlayers.Should().Be(2);
        leaderboard.Leaderboard.Should().HaveCount(2);
        leaderboard.Leaderboard[0].Nickname.Should().Be("alpha_lifecycle");
        leaderboard.Leaderboard[0].Kills.Should().Be(5);
        leaderboard.Leaderboard[0].Deaths.Should().Be(2);
        leaderboard.Leaderboard[0].GamesPlayed.Should().Be(1);
        leaderboard.Leaderboard[1].Nickname.Should().Be("bravo_lifecycle");

        var playerStatResponse = await client.GetAsync($"/leaderboard/{alpha.PlayerId}");
        playerStatResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var playerStat = await ApiTestHelpers.ReadAsAsync<PlayerStatResponse>(playerStatResponse);
        playerStat.Nickname.Should().Be("alpha_lifecycle");
        playerStat.Kills.Should().Be(5);
        playerStat.Deaths.Should().Be(2);
        playerStat.GamesPlayed.Should().Be(1);

        await factory.ExecuteDbContextAsync(async db =>
        {
            var storedSession = await db.Games.FindAsync(session.SessionId);

            storedSession.Should().NotBeNull();
            storedSession!.State.Should().Be(GameState.Finished);
            storedSession.PlayerCount.Should().Be(2);
            storedSession.StartTime.Should().NotBeNull();
        });
    }

    [Fact(DisplayName = "Integration: auth endpoints register, login and verify work together")]
    public async Task AuthEndpoints_RegisterLoginAndVerify_WorkTogether()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/auth/register", new LoginRequest("auth_user", "Pass123!"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var registeredUser = await ApiTestHelpers.ReadAsAsync<AuthResponse>(registerResponse);
        registeredUser.PlayerId.Should().BeGreaterThan(0);
        registeredUser.Username.Should().Be("auth_user");
        registeredUser.Token.Should().NotBeNullOrWhiteSpace();

        var loginResponse = await client.PostAsJsonAsync("/auth/login", new LoginRequest("auth_user", "Pass123!"));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loggedInUser = await ApiTestHelpers.ReadAsAsync<AuthResponse>(loginResponse);
        loggedInUser.PlayerId.Should().Be(registeredUser.PlayerId);
        loggedInUser.Username.Should().Be("auth_user");
        loggedInUser.Token.Should().NotBeNullOrWhiteSpace();

        await ApiTestHelpers.AuthorizeAsync(client, loggedInUser.Token);

        var verifyResponse = await client.GetAsync("/auth/verify");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verification = await ApiTestHelpers.ReadAsAsync<VerifyTokenResponse>(verifyResponse);
        verification.Valid.Should().BeTrue();
        verification.PlayerId.Should().Be(registeredUser.PlayerId);
        verification.Username.Should().Be("auth_user");
    }

    [Fact(DisplayName = "Integration: leaderboard aggregates stats across three players and two matches")]
    public async Task LeaderboardEndpoints_WithThreePlayersAndTwoMatches_ReturnExpectedStats()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        var alpha = await ApiTestHelpers.RegisterUserAsync(client, "alpha_top", "secret123");
        var bravo = await ApiTestHelpers.RegisterUserAsync(client, "bravo_top", "secret123");
        var charlie = await ApiTestHelpers.RegisterUserAsync(client, "charlie_top", "secret123");

        await ApiTestHelpers.CompleteMatchAsync(client, "10.0.1.1", 7101,
            new MatchResult(alpha.PlayerId, 7, 2),
            new MatchResult(bravo.PlayerId, 3, 4),
            new MatchResult(charlie.PlayerId, 1, 5));

        await ApiTestHelpers.CompleteMatchAsync(client, "10.0.1.2", 7102,
            new MatchResult(alpha.PlayerId, 1, 3),
            new MatchResult(bravo.PlayerId, 6, 1),
            new MatchResult(charlie.PlayerId, 4, 2));

        await ApiTestHelpers.AuthorizeAsync(client, alpha.Token);

        var leaderboardResponse = await client.GetAsync($"/leaderboard?players_limit=2&player_id={alpha.PlayerId}");
        leaderboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var leaderboard = await ApiTestHelpers.ReadAsAsync<LeaderboardEnvelope>(leaderboardResponse);
        leaderboard.TotalPlayers.Should().Be(3);
        leaderboard.Leaderboard.Should().HaveCount(2);
        leaderboard.Leaderboard[0].Nickname.Should().Be("bravo_top");
        leaderboard.Leaderboard[0].Kills.Should().Be(9);
        leaderboard.Leaderboard[0].GamesPlayed.Should().Be(2);
        leaderboard.Leaderboard[1].Nickname.Should().Be("alpha_top");
        leaderboard.Leaderboard[1].Kills.Should().Be(8);
        leaderboard.Leaderboard[1].GamesPlayed.Should().Be(2);

        var playerStatResponse = await client.GetAsync($"/leaderboard/{charlie.PlayerId}");
        playerStatResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var charlieStat = await ApiTestHelpers.ReadAsAsync<PlayerStatResponse>(playerStatResponse);
        charlieStat.Nickname.Should().Be("charlie_top");
        charlieStat.Kills.Should().Be(5);
        charlieStat.Deaths.Should().Be(7);
        charlieStat.GamesPlayed.Should().Be(2);
    }

    [Fact(DisplayName = "Integration: join returns 500 when stored connection data is corrupted")]
    public async Task JoinGame_WhenConnectionDataContainsInvalidJson_ReturnsInternalServerError()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Games.Add(new GameSession
            {
                State = GameState.Waiting,
                PlayerCount = 1,
                MaxPlayers = 10,
                ConnectionData = "NOT_JSON"
            });

            await db.SaveChangesAsync();
        });

        var user = await ApiTestHelpers.RegisterUserAsync(client, "broken_join_user", "secret123");
        await ApiTestHelpers.AuthorizeAsync(client, user.Token);

        var joinResponse = await client.GetAsync("/games/join");
        joinResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var error = await ApiTestHelpers.ReadAsAsync<ErrorResponse>(joinResponse);
        error.Error.Should().Be("Server data corruption");
    }

    [Fact(DisplayName = "Integration: join selects the most filled active session")]
    public async Task JoinGame_WithMixedSessionStates_ReturnsMostFilledActiveSession()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Games.AddRange(
                ApiTestHelpers.CreateSession(GameState.Waiting, 3, 10, "10.0.2.1", 7201, "waiting-3"),
                ApiTestHelpers.CreateSession(GameState.Playing, 5, 10, "10.0.2.2", 7202, "playing-5"),
                ApiTestHelpers.CreateSession(GameState.Waiting, 10, 10, "10.0.2.3", 7203, "waiting-full"),
                ApiTestHelpers.CreateSession(GameState.Finished, 1, 10, "10.0.2.4", 7204, "finished-1"));

            await db.SaveChangesAsync();
        });

        var user = await ApiTestHelpers.RegisterUserAsync(client, "join_selector_user", "secret123");
        await ApiTestHelpers.AuthorizeAsync(client, user.Token);

        var joinResponse = await client.GetAsync("/games/join");
        joinResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var connection = await ApiTestHelpers.ReadAsAsync<ConnectionInfo>(joinResponse);
        connection.Ip.Should().Be("10.0.2.2");
        connection.Port.Should().Be(7202);
        connection.Key.Should().Be("playing-5");
    }
}
