using GameServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GameServer.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Player> Players { get; set; }
        public DbSet<GameSession> Games { get; set; }
        public DbSet<LeaderboardStat> Leaderboard { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Player>()
                .HasIndex(p => p.Username)
                .IsUnique();
        }
    }
}
