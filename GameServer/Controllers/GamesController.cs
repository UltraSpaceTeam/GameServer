using System.Text.Json;
using GameServer.Data;
using GameServer.DTOs;
using GameServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameServer.Controllers
{
    [ApiController]
    [Route("games")]
    public class GamesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<GamesController> _logger;

        public GamesController(ApplicationDbContext context, ILogger<GamesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> RegisterSession([FromBody] RegisterSessionRequest request)
        {
            string ip = request.IpAddress;
            if (string.IsNullOrEmpty(ip))
            {
                var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
                ip = (remoteIp == "::1") ? "127.0.0.1" : remoteIp;
            }

            string sessionKey = Guid.NewGuid().ToString("N");

            var connInfo = new DTOs.ConnectionInfo
            {
                Ip = ip,
                Port = request.Port,
                Key = sessionKey
            };

            var session = new GameSession
            {
                State = GameState.Waiting,
                PlayerCount = 0,
                MaxPlayers = request.MaxPlayers,
                ConnectionData = JsonSerializer.Serialize(connInfo)
            };

            _context.Games.Add(session);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"New Session Registered: ID={session.Id}, IP={ip}:{request.Port}");

            return Ok(new RegisterSessionResponse
            {
                SessionId = session.Id,
                Key = sessionKey
            });
        }

        [Authorize]
        [HttpGet("join")]
        public async Task<IActionResult> JoinGame()
        {
            var session = await _context.Games
                .Where(g => g.State == GameState.Waiting && g.PlayerCount < g.MaxPlayers)
                .OrderByDescending(g => g.PlayerCount)
                .FirstOrDefaultAsync();

            if (session == null)
            {
                return NotFound(new { error = "No active game sessions found. Please try again later." });
            }

            try
            {
                var connInfo = JsonSerializer.Deserialize<DTOs.ConnectionInfo>(session.ConnectionData);
                return Ok(connInfo);
            }
            catch
            {
                _logger.LogError($"Corrupted ConnectionData for session {session.Id}");
                return StatusCode(500, new { error = "Server data corruption" });
            }
        }

        [HttpPost("result")]
        public async Task<IActionResult> SubmitResult([FromBody] GameResultRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var session = await _context.Games.FindAsync(request.SessionId);

                if (session == null)
                {
                    return NotFound(new { error = "Session not found" });
                }

                if (session.State == GameState.Finished)
                {
                    _logger.LogWarning($"Attempt to submit results for already finished session {request.SessionId}");
                    return Ok();
                }

                var playerIds = request.Leaderboard.Select(r => r.PlayerId).ToList();

                var statsList = await _context.Leaderboard
                    .Where(s => playerIds.Contains(s.PlayerId))
                    .ToListAsync();

                foreach (var result in request.Leaderboard)
                {
                    var stat = statsList.FirstOrDefault(s => s.PlayerId == result.PlayerId);

                    if (stat != null)
                    {
                        stat.Kills += result.Kills;
                        stat.Deaths += result.Deaths;
                        stat.GamesPlayed += 1;
                    }
                    else
                    {
                        _logger.LogWarning($"Stats not found for player {result.PlayerId}");
                    }
                }

                session.State = GameState.Finished;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation($"Session {request.SessionId} finished successfully. Stats updated for {statsList.Count} players.");
                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error saving game results for session {request.SessionId}");
                return StatusCode(500, new { error = "Database transaction failed" });
            }
        }

        [HttpPost("player_joined")]
        public async Task<IActionResult> PlayerJoined([FromBody] PlayerJoinedRequest request)
        {
            var session = await _context.Games.FindAsync(request.SessionId);
            if (session == null)
            {
                session = new GameSession
                {
                    Id = request.SessionId,
                    State = GameState.Waiting,
                    PlayerCount = 0
                };
                _context.Games.Add(session);
            }

            session.PlayerCount++;

            _logger.LogInformation($"Player joined Session {session.Id}. Count: {session.PlayerCount}");

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("healthcheck")]
        public async Task<IActionResult> HealthCheck([FromBody] HealthCheckRequest request)
        {
            var session = await _context.Games.FindAsync(request.SessionId);
            if (session == null) return NotFound(new { error = "Session not found" });
            session.LastHeartbeat = DateTime.UtcNow;

            if (Enum.TryParse<GameState>(request.State, true, out var newState))
            {
                if (session.State == GameState.Waiting && newState == GameState.Playing)
                {
                    session.StartTime = DateTime.UtcNow;
                    _logger.LogInformation($"Session {session.Id} STARTED at {session.StartTime}");
                }
                session.State = newState;
            }

            if (request.Players != null)
            {
                session.PlayerCount = request.Players.Count;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
