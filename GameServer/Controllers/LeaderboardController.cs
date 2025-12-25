using GameServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameServer.Controllers
{
    [Authorize]
    [ApiController]
    [Route("leaderboard")]
    public class LeaderboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LeaderboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /leaderboard?players_limit=10&player_id=1
        [HttpGet]
        public async Task<IActionResult> GetLeaderboard([FromQuery(Name = "players_limit")] int limit = 10, [FromQuery(Name = "player_id")] int playerId = 0)
        {
            if (playerId == 0) return BadRequest(new { error = "player_id is required" });

            var topPlayers = await _context.Leaderboard
                .Include(l => l.Player)
                .OrderByDescending(l => l.Kills)
                .Take(limit)
                .Select(l => new
                {
                    nickname = l.Player!.Username,
                    kills = l.Kills,
                    deaths = l.Deaths,
                    gamesPlayed = l.GamesPlayed
                })
                .ToListAsync();

            var total = await _context.Players.CountAsync();

            return Ok(new { leaderboard = topPlayers, totalPlayers = total });
        }

        // GET /leaderboard/{player_id}
        [HttpGet("{player_id}")]
        public async Task<IActionResult> GetPlayerStat(int player_id)
        {
            var stat = await _context.Leaderboard
                .Include(l => l.Player)
                .FirstOrDefaultAsync(l => l.PlayerId == player_id);

            if (stat == null)
                return NotFound(new { error = "player not found" });

            return Ok(new
            {
                nickname = stat.Player!.Username,
                kills = stat.Kills,
                deaths = stat.Deaths,
                gamesPlayed = stat.GamesPlayed
            });
        }
    }
}
