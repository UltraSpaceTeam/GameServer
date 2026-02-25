using System.Text.Json;
using BCrypt.Net;
using GameServer.Data;
using GameServer.DTOs;
using GameServer.Models;

namespace GameServer.Tests.TestUtils;

public static class Seed
{
    public static Player AddPlayer(ApplicationDbContext ctx, string username, string password)
    {
        var player = new Player
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };
        ctx.Players.Add(player);
        ctx.SaveChanges();

        ctx.Leaderboard.Add(new LeaderboardStat { PlayerId = player.Id });
        ctx.SaveChanges();

        return player;
    }

    public static LeaderboardStat AddLeaderboard(ApplicationDbContext ctx, Player player, int kills, int deaths, int gamesPlayed)
    {
        var stat = ctx.Leaderboard.First(s => s.PlayerId == player.Id);
        stat.Kills = kills;
        stat.Deaths = deaths;
        stat.GamesPlayed = gamesPlayed;
        ctx.SaveChanges();
        return stat;
    }

    public static GameSession AddSession(ApplicationDbContext ctx, GameState state, int playerCount, int maxPlayers, string ip = "127.0.0.1", int port = 7777, string key = "k")
    {
        var conn = new ConnectionInfo { Ip = ip, Port = port, Key = key };
        var session = new GameSession
        {
            State = state,
            PlayerCount = playerCount,
            MaxPlayers = maxPlayers,
            ConnectionData = JsonSerializer.Serialize(conn),
            LastHeartbeat = DateTime.UtcNow
        };

        ctx.Games.Add(session);
        ctx.SaveChanges();
        return session;
    }
}