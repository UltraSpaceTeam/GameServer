using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameServer.DTOs;
using GameServer.Models;
using GameServer.Tests.TestUtils;

namespace GameServer.Tests.Systems;

public class ApiSystemTests
{
    [Fact(DisplayName = "System: auth registration, login and bearer verification form a valid flow")]
    public async Task AuthFlow_RegisterLoginAndVerify_ReturnsConsistentJwtPayload()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        const string username = "system_auth_user";
        const string password = "Pass123!";

        var registerResponse = await client.PostAsJsonAsync("/auth/register", new LoginRequest(username, password));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var registeredUser = await ApiTestHelpers.ReadAsAsync<AuthResponse>(registerResponse);
        registeredUser.PlayerId.Should().BeGreaterThan(0);
        registeredUser.Username.Should().Be(username);
        registeredUser.Token.Should().NotBeNullOrWhiteSpace();

        var loginResponse = await client.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loggedInUser = await ApiTestHelpers.ReadAsAsync<AuthResponse>(loginResponse);
        loggedInUser.PlayerId.Should().Be(registeredUser.PlayerId);
        loggedInUser.Username.Should().Be(username);
        loggedInUser.Token.Should().NotBeNullOrWhiteSpace();

        var tokenHandler = new JwtSecurityTokenHandler();
        tokenHandler.CanReadToken(loggedInUser.Token).Should().BeTrue();

        var token = tokenHandler.ReadJwtToken(loggedInUser.Token);
        token.Claims.Should().Contain(claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == registeredUser.PlayerId.ToString());
        token.Claims.Should().Contain(claim => claim.Type == JwtRegisteredClaimNames.UniqueName && claim.Value == username);

        await ApiTestHelpers.AuthorizeAsync(client, loggedInUser.Token);

        var verifyResponse = await client.GetAsync("/auth/verify");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verification = await ApiTestHelpers.ReadAsAsync<VerifyTokenResponse>(verifyResponse);
        verification.Valid.Should().BeTrue();
        verification.PlayerId.Should().Be(registeredUser.PlayerId);
        verification.Username.Should().Be(username);
    }

    [Fact(DisplayName = "System: session lifecycle updates heartbeat and exposes finished match data in leaderboard")]
    public async Task MatchLifecycle_RegisterPlayFinishAndLeaderboard_ReturnsExpectedState()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        var alpha = await ApiTestHelpers.RegisterUserAsync(client, "system_alpha_lifecycle", "secret123");
        var bravo = await ApiTestHelpers.RegisterUserAsync(client, "system_bravo_lifecycle", "secret123");

        await ApiTestHelpers.AuthorizeAsync(client, alpha.Token);

        var registerResponse = await client.PostAsJsonAsync("/games/register", new RegisterSessionRequest
        {
            Port = 7301,
            IpAddress = "10.1.0.10",
            MaxPlayers = 4
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await ApiTestHelpers.ReadAsAsync<RegisterSessionResponse>(registerResponse);

        var initialSession = await factory.ExecuteDbContextAsync(db => db.Games.FindAsync(session.SessionId).AsTask());
        initialSession.Should().NotBeNull();
        initialSession!.StartTime.Should().BeNull();
        var initialHeartbeat = initialSession.LastHeartbeat;

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

        await factory.ExecuteDbContextAsync(async db =>
        {
            var playingSession = await db.Games.FindAsync(session.SessionId);

            playingSession.Should().NotBeNull();
            playingSession!.State.Should().Be(GameState.Playing);
            playingSession.StartTime.Should().NotBeNull();
            playingSession.LastHeartbeat.Should().BeOnOrAfter(initialHeartbeat);
            playingSession.PlayerCount.Should().Be(2);
        });

        var resultResponse = await client.PostAsJsonAsync("/games/result", new GameResultRequest
        {
            SessionId = session.SessionId,
            Leaderboard =
            [
                new PlayerResultDto { PlayerId = alpha.PlayerId, Kills = 4, Deaths = 1 },
                new PlayerResultDto { PlayerId = bravo.PlayerId, Kills = 1, Deaths = 4 }
            ]
        });

        resultResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var leaderboardResponse = await client.GetAsync($"/leaderboard?players_limit=10&player_id={alpha.PlayerId}");
        leaderboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var leaderboard = await ApiTestHelpers.ReadAsAsync<LeaderboardEnvelope>(leaderboardResponse);
        leaderboard.TotalPlayers.Should().Be(2);
        leaderboard.Leaderboard.Should().HaveCount(2);
        leaderboard.Leaderboard[0].Nickname.Should().Be("system_alpha_lifecycle");
        leaderboard.Leaderboard[0].Kills.Should().Be(4);
        leaderboard.Leaderboard[0].Deaths.Should().Be(1);
        leaderboard.Leaderboard[0].GamesPlayed.Should().Be(1);
        leaderboard.Leaderboard[1].Nickname.Should().Be("system_bravo_lifecycle");

        var playerStatResponse = await client.GetAsync($"/leaderboard/{alpha.PlayerId}");
        playerStatResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var playerStat = await ApiTestHelpers.ReadAsAsync<PlayerStatResponse>(playerStatResponse);
        playerStat.Nickname.Should().Be("system_alpha_lifecycle");
        playerStat.Kills.Should().Be(4);
        playerStat.Deaths.Should().Be(1);
        playerStat.GamesPlayed.Should().Be(1);
    }

    [Fact(DisplayName = "System: join returns connection info for the most filled active session")]
    public async Task JoinGame_WithPreparedSessions_ReturnsExpectedConnectionInfo()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        var sessions = new[]
        {
            new SessionCandidate(GameState.Waiting, 4, 10, "10.2.0.1", 7401, "waiting-4"),
            new SessionCandidate(GameState.Playing, 7, 10, "10.2.0.2", 7402, "playing-7"),
            new SessionCandidate(GameState.Waiting, 10, 10, "10.2.0.3", 7403, "waiting-full"),
            new SessionCandidate(GameState.Finished, 2, 10, "10.2.0.4", 7404, "finished-2")
        };

        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Games.AddRange(sessions.Select(session => ApiTestHelpers.CreateSession(
                session.State,
                session.PlayerCount,
                session.MaxPlayers,
                session.Ip,
                session.Port,
                session.Key)));

            await db.SaveChangesAsync();
        });

        var user = await ApiTestHelpers.RegisterUserAsync(client, "system_join_user", "secret123");
        await ApiTestHelpers.AuthorizeAsync(client, user.Token);

        var joinResponse = await client.GetAsync("/games/join");
        joinResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var connection = await ApiTestHelpers.ReadAsAsync<ConnectionInfo>(joinResponse);
        var expectedSession = sessions
            .Where(session => (session.State == GameState.Waiting || session.State == GameState.Playing) && session.PlayerCount < session.MaxPlayers)
            .OrderByDescending(session => session.PlayerCount)
            .First();

        connection.Ip.Should().Be(expectedSession.Ip);
        connection.Port.Should().Be(expectedSession.Port);
        connection.Key.Should().Be(expectedSession.Key);
    }

    [Fact(DisplayName = "System: duplicate result submission does not double count leaderboard statistics")]
    public async Task SubmitResult_TwiceForSameSession_KeepsLeaderboardIdempotent()
    {
        using var factory = new IntegrationTestWebAppFactory();
        using var client = factory.CreateClient();

        var alpha = await ApiTestHelpers.RegisterUserAsync(client, "system_alpha_idempotent", "secret123");
        var bravo = await ApiTestHelpers.RegisterUserAsync(client, "system_bravo_idempotent", "secret123");

        await ApiTestHelpers.AuthorizeAsync(client, alpha.Token);

        var registerResponse = await client.PostAsJsonAsync("/games/register", new RegisterSessionRequest
        {
            Port = 7501,
            IpAddress = "10.3.0.10",
            MaxPlayers = 4
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await ApiTestHelpers.ReadAsAsync<RegisterSessionResponse>(registerResponse);

        (await client.PostAsJsonAsync("/games/player_joined", new PlayerJoinedRequest(session.SessionId, alpha.PlayerId)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/games/player_joined", new PlayerJoinedRequest(session.SessionId, bravo.PlayerId)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/games/healthcheck", new HealthCheckRequest(
            session.SessionId,
            "Playing",
            "12:00:00",
            [alpha.PlayerId, bravo.PlayerId])))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var resultRequest = new GameResultRequest
        {
            SessionId = session.SessionId,
            Leaderboard =
            [
                new PlayerResultDto { PlayerId = alpha.PlayerId, Kills = 6, Deaths = 2 },
                new PlayerResultDto { PlayerId = bravo.PlayerId, Kills = 2, Deaths = 6 }
            ]
        };

        (await client.PostAsJsonAsync("/games/result", resultRequest))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var beforeDuplicateResponse = await client.GetAsync($"/leaderboard?players_limit=10&player_id={alpha.PlayerId}");
        beforeDuplicateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeDuplicate = await ApiTestHelpers.ReadAsAsync<LeaderboardEnvelope>(beforeDuplicateResponse);

        (await client.PostAsJsonAsync("/games/result", resultRequest))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var afterDuplicateResponse = await client.GetAsync($"/leaderboard?players_limit=10&player_id={alpha.PlayerId}");
        afterDuplicateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDuplicate = await ApiTestHelpers.ReadAsAsync<LeaderboardEnvelope>(afterDuplicateResponse);

        var alphaBefore = beforeDuplicate.Leaderboard.Single(player => player.Nickname == "system_alpha_idempotent");
        var bravoBefore = beforeDuplicate.Leaderboard.Single(player => player.Nickname == "system_bravo_idempotent");
        var alphaAfter = afterDuplicate.Leaderboard.Single(player => player.Nickname == "system_alpha_idempotent");
        var bravoAfter = afterDuplicate.Leaderboard.Single(player => player.Nickname == "system_bravo_idempotent");

        alphaAfter.Kills.Should().Be(alphaBefore.Kills);
        alphaAfter.Deaths.Should().Be(alphaBefore.Deaths);
        alphaAfter.GamesPlayed.Should().Be(alphaBefore.GamesPlayed);
        alphaAfter.Kills.Should().Be(6);
        alphaAfter.Deaths.Should().Be(2);
        alphaAfter.GamesPlayed.Should().Be(1);

        bravoAfter.Kills.Should().Be(bravoBefore.Kills);
        bravoAfter.Deaths.Should().Be(bravoBefore.Deaths);
        bravoAfter.GamesPlayed.Should().Be(bravoBefore.GamesPlayed);
        bravoAfter.Kills.Should().Be(2);
        bravoAfter.Deaths.Should().Be(6);
        bravoAfter.GamesPlayed.Should().Be(1);
    }

    private sealed record SessionCandidate(GameState State, int PlayerCount, int MaxPlayers, string Ip, int Port, string Key);
}
