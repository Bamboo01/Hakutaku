// This is just sample code, feel free to overwrite it.

using Microsoft.EntityFrameworkCore;

namespace server.Models
{
    // Create a POD struct
    public class Player
    {
        public Guid Id { get; set; }
        public string DeviceId { get; set; } = "";
        public int Xp { get; set; }
    }

    // A Player can have multiple characters. No independent progress/stats of
    // their own yet -- that's tracked on Player -- so this is just identity.
    public class Character
    {
        public Guid Id { get; set; }
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public string Name { get; set; } = "";
    }

    // Telemetry is player-scoped, not character-scoped (see CLAUDE.md).
    // Data holds the event-specific payload as JSON.
    public class TelemetryEvent
    {
        public Guid Id { get; set; }
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public string EventType { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string Data { get; set; } = "";
    }

    public class Db : DbContext
    {
        public Db(DbContextOptions<Db> options) : base(options) { }

        // C# magic, serializes a POD struct into a database set
        public DbSet<Player> Players => Set<Player>();
        public DbSet<Character> Characters => Set<Character>();
        public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();
        public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
        public DbSet<AdminSession> AdminSessions => Set<AdminSession>();

        // Admin tables follow the TDD conventions (bigint identity keys, snake_case names).
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AdminUser>(e =>
            {
                e.ToTable("admin_users", t => t.HasCheckConstraint("ck_admin_users_role", "role IN (0, 1)"));
                e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
                e.Property(x => x.Email).HasColumnName("email");
                e.Property(x => x.PwHash).HasColumnName("pw_hash");
                e.Property(x => x.Role).HasColumnName("role");
                e.Property(x => x.TotpSecret).HasColumnName("totp_secret");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.CreatedBy).HasColumnName("created_by");
                e.Property(x => x.DisabledAt).HasColumnName("disabled_at");
                e.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<AdminSession>(e =>
            {
                e.ToTable("admin_sessions");
                e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
                e.Property(x => x.AdminId).HasColumnName("admin_id");
                e.Property(x => x.TokenHash).HasColumnName("token_hash");
                e.Property(x => x.Ip).HasColumnName("ip");
                e.Property(x => x.IssuedAt).HasColumnName("issued_at").HasDefaultValueSql("now()");
                e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
                e.HasIndex(x => x.TokenHash).IsUnique();
                e.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
