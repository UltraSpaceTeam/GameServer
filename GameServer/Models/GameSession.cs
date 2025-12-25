using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace GameServer.Models
{
    public enum GameState { Waiting, Playing, Finished }

    [Table("games")]
    public class GameSession
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("state")]
        public GameState State { get; set; }

        [Column("connection_data")]
        public string ConnectionData { get; set; } = string.Empty;

        [Column("player_count")]
        public int PlayerCount { get; set; } = 0;

        [Column("max_players")]
        public int MaxPlayers { get; set; } = 20;

        [Column("start_time")]
        public DateTime? StartTime { get; set; }
        [Column("last_heartbeat")]
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    }
}
