// This is just sample code, feel free to overwrite it.

using Microsoft.EntityFrameworkCore;

namespace server.Models
{
    // Create a POD struct. How a player proves who they are lives in
    // PlayerIdentity (a device UUID, an email, ...), not on this row.
    public class Player
    {
        public Guid Id { get; set; }
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
        public DbSet<PlayerIdentity> PlayerIdentities => Set<PlayerIdentity>();
        public DbSet<PlayerSession> PlayerSessions => Set<PlayerSession>();

        // The admin and player-auth tables follow the TDD conventions (snake_case names,
        // bigint identity keys where a row has its own key).
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlayerIdentity>(e =>
            {
                e.ToTable("player_identities", t => t.HasCheckConstraint("ck_player_identities_provider", "provider IN (0, 1, 2)"));
                // (provider, subject) is the key because one device, email or Steam account
                // must map to at most one player -- and it makes find-or-create race-safe.
                e.HasKey(x => new { x.Provider, x.Subject });
                e.Property(x => x.PlayerId).HasColumnName("player_id");
                e.Property(x => x.Provider).HasColumnName("provider");
                e.Property(x => x.Subject).HasColumnName("subject");
                e.Property(x => x.Email).HasColumnName("email");
                e.Property(x => x.PwHash).HasColumnName("pw_hash");
                e.Property(x => x.LinkedAt).HasColumnName("linked_at").HasDefaultValueSql("now()");
                // At most one email per player, enforced here rather than only in the handler.
                e.HasIndex(x => x.PlayerId).HasFilter("provider = 1").IsUnique().HasDatabaseName("ux_player_identities_one_email");
                e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PlayerSession>(e =>
            {
                e.ToTable("player_sessions");
                e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
                e.Property(x => x.PlayerId).HasColumnName("player_id");
                e.Property(x => x.TokenHash).HasColumnName("token_hash");
                e.Property(x => x.IssuedAt).HasColumnName("issued_at").HasDefaultValueSql("now()");
                e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
                e.HasIndex(x => x.TokenHash).IsUnique();
                e.HasIndex(x => x.PlayerId).HasFilter("revoked_at IS NULL");
                e.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AdminUser>(e =>
            {
                e.ToTable("admin_users", t => t.HasCheckConstraint("ck_admin_users_role", "role IN (0, 1)"));
                e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
                e.Property(x => x.Username).HasColumnName("username");
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
                e.Property(x => x.IdleExpiresAt).HasColumnName("idle_expires_at");
                e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
                e.HasIndex(x => x.TokenHash).IsUnique();
                e.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
