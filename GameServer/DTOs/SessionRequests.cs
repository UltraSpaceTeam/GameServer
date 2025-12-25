namespace GameServer.DTOs
{
    public class GameResultRequest
    {
        public int SessionId { get; set; }
        public List<PlayerResultDto> Leaderboard { get; set; } = new();
    }

    public class PlayerResultDto
    {
        public int PlayerId { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
    }

    public record PlayerJoinedRequest(int SessionId, int PlayerId);

    public record HealthCheckRequest(int SessionId, string State, string Time, List<int> Players);
}
