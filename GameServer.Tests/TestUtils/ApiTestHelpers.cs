using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using GameServer.DTOs;
using GameServer.Models;

namespace GameServer.Tests.TestUtils;

internal static class ApiTestHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<AuthResponse> RegisterUserAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new LoginRequest(username, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadAsAsync<AuthResponse>(response);
    }

    public static Task AuthorizeAsync(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Task.CompletedTask;
    }

    public static async Task CompleteMatchAsync(HttpClient client, string ipAddress, int port, params MatchResult[] results)
    {
        var registerResponse = await client.PostAsJsonAsync("/games/register", new RegisterSessionRequest
        {
            Port = port,
            IpAddress = ipAddress,
            MaxPlayers = 10
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await ReadAsAsync<RegisterSessionResponse>(registerResponse);

        foreach (var result in results)
        {
            var playerJoinedResponse = await client.PostAsJsonAsync("/games/player_joined", new PlayerJoinedRequest(session.SessionId, result.PlayerId));
            playerJoinedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var healthcheckResponse = await client.PostAsJsonAsync("/games/healthcheck", new HealthCheckRequest(
            session.SessionId,
            "Playing",
            "12:00:00",
            results.Select(result => result.PlayerId).ToList()));

        healthcheckResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResultResponse = await client.PostAsJsonAsync("/games/result", new GameResultRequest
        {
            SessionId = session.SessionId,
            Leaderboard = results
                .Select(result => new PlayerResultDto
                {
                    PlayerId = result.PlayerId,
                    Kills = result.Kills,
                    Deaths = result.Deaths
                })
                .ToList()
        });

        submitResultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public static async Task<T> ReadAsAsync<T>(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        payload.Should().NotBeNull();
        return payload!;
    }

    public static GameSession CreateSession(GameState state, int playerCount, int maxPlayers, string ip, int port, string key)
    {
        return new GameSession
        {
            State = state,
            PlayerCount = playerCount,
            MaxPlayers = maxPlayers,
            ConnectionData = JsonSerializer.Serialize(new ConnectionInfo
            {
                Ip = ip,
                Port = port,
                Key = key
            }),
            LastHeartbeat = DateTime.UtcNow
        };
    }
}

internal sealed record LeaderboardEnvelope(List<PlayerStatResponse> Leaderboard, int TotalPlayers);

internal sealed record PlayerStatResponse(string Nickname, int Kills, int Deaths, int GamesPlayed);

internal sealed record MatchResult(int PlayerId, int Kills, int Deaths);

internal sealed record ErrorResponse(string Error);

internal sealed record VerifyTokenResponse(
    bool Valid,
    [property: JsonPropertyName("player_id")] int PlayerId,
    string Username);
