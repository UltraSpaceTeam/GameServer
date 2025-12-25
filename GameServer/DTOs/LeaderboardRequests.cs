using System.Text.Json.Serialization;

namespace GameServer.DTOs
{
    public class LeaderboardRequest
    {
        [JsonPropertyName("player_id")]
        public int PlayerId { get; set; }
    }
}
