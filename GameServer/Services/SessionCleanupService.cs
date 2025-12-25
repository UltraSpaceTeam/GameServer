using GameServer.Data;
using GameServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GameServer.Services
{
    public class SessionCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SessionCleanupService> _logger;

        private readonly TimeSpan _timeoutLimit = TimeSpan.FromSeconds(60);
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(10);

        public SessionCleanupService(IServiceProvider serviceProvider, ILogger<SessionCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Session Cleanup Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndCleanupSessions(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during session cleanup.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task CheckAndCleanupSessions(CancellationToken token)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var threshold = DateTime.UtcNow - _timeoutLimit;

                var deadSessions = await context.Games
                    .Where(g => g.State != GameState.Finished && g.LastHeartbeat < threshold)
                    .ToListAsync(token);

                if (deadSessions.Any())
                {
                    foreach (var session in deadSessions)
                    {
                        session.State = GameState.Finished;

                        _logger.LogWarning($"Session {session.Id} timed out. Last heartbeat: {session.LastHeartbeat}. Marked as Finished.");
                    }

                    await context.SaveChangesAsync(token);
                }
            }
        }
    }
}
