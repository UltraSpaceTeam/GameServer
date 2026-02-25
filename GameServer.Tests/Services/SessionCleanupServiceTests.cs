using System.Reflection;
using FluentAssertions;
using GameServer.Data;
using GameServer.Models;
using GameServer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameServer.Tests.Services;

public class SessionCleanupServiceTests
{
    private static async Task InvokePrivateCleanup(SessionCleanupService service, CancellationToken token)
    {
        var mi = typeof(SessionCleanupService).GetMethod(
            "CheckAndCleanupSessions",
            BindingFlags.Instance | BindingFlags.NonPublic);

        mi.Should().NotBeNull("Private method CheckAndCleanupSessions must exist");

        var task = (Task)mi!.Invoke(service, new object[] { token })!;
        await task;
    }

    [Fact]
    public async Task CheckAndCleanupSessions_WhenDeadSessionsExist_MarksThemFinished()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(opt =>
            opt.UseSqlite(conn));

        var logger = new Mock<ILogger<SessionCleanupService>>();
        services.AddSingleton(logger.Object);

        var provider = services.BuildServiceProvider();

        using (var seedScope = provider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await ctx.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;

            ctx.Games.AddRange(
                new GameSession
                {
                    State = GameState.Waiting,
                    PlayerCount = 1,
                    MaxPlayers = 10,
                    ConnectionData = "{}",
                    LastHeartbeat = now.AddSeconds(-120)
                },
                new GameSession
                {
                    State = GameState.Playing,
                    PlayerCount = 2,
                    MaxPlayers = 10,
                    ConnectionData = "{}",
                    LastHeartbeat = now.AddSeconds(-61) 
                },
                new GameSession
                {
                    State = GameState.Playing,
                    PlayerCount = 2,
                    MaxPlayers = 10,
                    ConnectionData = "{}",
                    LastHeartbeat = now.AddSeconds(-10)
                },
                new GameSession
                {
                    State = GameState.Finished,
                    PlayerCount = 0,
                    MaxPlayers = 10,
                    ConnectionData = "{}",
                    LastHeartbeat = now.AddSeconds(-1000)
                }
            );

            await ctx.SaveChangesAsync();
        }

        var service = new SessionCleanupService(provider, logger.Object);

        await InvokePrivateCleanup(service, CancellationToken.None);

        using (var assertScope = provider.CreateScope())
        {
            var ctx = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var all = await ctx.Games.OrderBy(g => g.Id).ToListAsync();

            all.Should().HaveCount(4);

            all[0].State.Should().Be(GameState.Finished);

            all[1].State.Should().Be(GameState.Finished);

            all[2].State.Should().Be(GameState.Playing);

            all[3].State.Should().Be(GameState.Finished);
        }

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckAndCleanupSessions_WhenNoDeadSessions_DoesNotChangeStates()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(opt => opt.UseSqlite(conn));

        var logger = new Mock<ILogger<SessionCleanupService>>();
        services.AddSingleton(logger.Object);

        var provider = services.BuildServiceProvider();

        int sessionId;

        using (var seedScope = provider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await ctx.Database.EnsureCreatedAsync();

            var s = new GameSession
            {
                State = GameState.Playing,
                PlayerCount = 1,
                MaxPlayers = 10,
                ConnectionData = "{}",
                LastHeartbeat = DateTime.UtcNow.AddSeconds(-5)
            };

            ctx.Games.Add(s);
            await ctx.SaveChangesAsync();
            sessionId = s.Id;
        }

        var service = new SessionCleanupService(provider, logger.Object);

        await InvokePrivateCleanup(service, CancellationToken.None);

        using (var assertScope = provider.CreateScope())
        {
            var ctx = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var s = await ctx.Games.FindAsync(sessionId);
            s.Should().NotBeNull();
            s!.State.Should().Be(GameState.Playing);
        }

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}