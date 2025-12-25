using System.Text.Json.Serialization;

namespace GameServer.DTOs
{
    public record LoginRequest(string Username, string Password);
    public record AuthResponse(string Token, int PlayerId, string Username);
}
