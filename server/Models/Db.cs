using Microsoft.EntityFrameworkCore;

namespace server.Models
{
    public class Db : DbContext
    {
        public Db(DbContextOptions<Db> options) : base(options) { }

        // one DbSet per table
        public DbSet<Player> Players => Set<Player>();
        public DbSet<Character> Characters => Set<Character>();
        public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();
        public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
        public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
        public DbSet<PlayerIdentity> PlayerIdentities => Set<PlayerIdentity>();
        public DbSet<PlayerSession> PlayerSessions => Set<PlayerSession>();
        public DbSet<PlayerEmailCode> PlayerEmailCodes => Set<PlayerEmailCode>();

        // admin and player-auth tables follow the tdd conventions: snake_case names, and bigint
        // identity keys where a row has its own key
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlayerIdentity>(e =>
            {
                e.ToTable("player_identities", t => t.HasCheckConstraint("ck_player_identities_provider", "provider IN (0, 1, 2)"));
                // (provider, subject) is the key: one device token, email or steam account maps to at most
                // one player, and lookups by credential stay cheap
                e.HasKey(x => new { x.Provider, x.Subject });
                e.Property(x => x.PlayerId).HasColumnName("player_id");
                e.Property(x => x.Provider).HasColumnName("provider");
                e.Property(x => x.Subject).HasColumnName("subject");
                e.Property(x => x.Email).HasColumnName("email");
                e.Property(x => x.PwHash).HasColumnName("pw_hash");
                e.Property(x => x.LinkedAt).HasColumnName("linked_at").HasDefaultValueSql("now()");
                e.Property(x => x.VerifiedAt).HasColumnName("verified_at");
                // at most one email per player, enforced here and not only in the handler
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

            modelBuilder.Entity<PlayerEmailCode>(e =>
            {
                e.ToTable("player_email_codes", t => t.HasCheckConstraint("ck_player_email_codes_purpose", "purpose IN (0, 1)"));
                e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
                e.Property(x => x.PlayerId).HasColumnName("player_id");
                e.Property(x => x.Purpose).HasColumnName("purpose");
                e.Property(x => x.CodeHash).HasColumnName("code_hash");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                e.Property(x => x.Attempts).HasColumnName("attempts");
                e.Property(x => x.UsedAt).HasColumnName("used_at");
                e.HasIndex(x => new { x.PlayerId, x.Purpose, x.CreatedAt });
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
