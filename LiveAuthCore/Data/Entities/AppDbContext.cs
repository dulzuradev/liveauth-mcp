using LiveAuthCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Entities
{
    public class AppDbContext : DbContext
    {
        public DbSet<LoginAttempt> LoginAttempts { get; set; }

        public DbSet<RevokedToken> RevokedTokens { get; set; }

        public DbSet<MissionTask> MissionTasks { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    }
}
