using System.Text.Json.Serialization;

namespace GameServer.DTOs
{
    public class RegisterSessionRequest
    {
        public int Port { get; set; }
        public string? IpAddress { get; set; }
        public int MaxPlayers { get; set; } = 20;
    }

    public class RegisterSessionResponse
    {
        public int SessionId { get; set; }
        public required string Key { get; set; } 
    }

    public class ConnectionInfo
    {
        [JsonPropertyName("ip")]
        public required string Ip { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("key")]
        public required string Key { get; set; }
    }
}
