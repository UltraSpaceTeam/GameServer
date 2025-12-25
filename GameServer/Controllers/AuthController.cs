using GameServer.Data;
using GameServer.DTOs;
using GameServer.Models;
using GameServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameServer.Controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly TokenService _tokenService;

        public AuthController(ApplicationDbContext context, TokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] LoginRequest request)
        {
            try
            {
                if (await _context.Players.AnyAsync(p => p.Username == request.Username))
                    return Unauthorized(new { error = "You cannot use this login" });

                var player = new Player
                {
                    Username = request.Username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
                };

                _context.Players.Add(player);
                await _context.SaveChangesAsync();

                _context.Leaderboard.Add(new LeaderboardStat { PlayerId = player.Id });
                await _context.SaveChangesAsync();

                var token = _tokenService.CreateToken(player);

                return Ok(new AuthResponse(token, player.Id, player.Username));
            }
            catch (DbUpdateException)
            {
                return StatusCode(500, new { error = "Database error during registration" });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var player = await _context.Players.FirstOrDefaultAsync(p => p.Username == request.Username);

            if (player == null) {
                return Unauthorized(new { error = "user not found" });
            }
            if (!BCrypt.Net.BCrypt.Verify(request.Password, player.PasswordHash))
            {
                return Unauthorized(new { error = "Incorrect login/password" });
            }

            var token = _tokenService.CreateToken(player);
            return Ok(new AuthResponse(token, player.Id, player.Username));
        }
    }
}
