using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace GameServer.Models
{
    [Table("leaderboard")]
    public class LeaderboardStat
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("player_id")]
        public int PlayerId { get; set; }

        [ForeignKey("PlayerId")]
        public Player? Player { get; set; }

        [Column("kills")]
        public int Kills { get; set; }

        [Column("deaths")]
        public int Deaths { get; set; }

        [Column("games_played")]
        public int GamesPlayed { get; set; }
    }
}
