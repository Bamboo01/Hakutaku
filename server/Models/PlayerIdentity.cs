namespace server.Models
{
    public enum PlayerProvider : short
    {
        Device = 0,
        Email = 1,
        Steam = 2,
    }

    // One way a player can prove who they are. A player can have several (a device
    // plus an email, say) -- linking adds a row, it never replaces one.
    public class PlayerIdentity
    {
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public PlayerProvider Provider { get; set; }
        // The provider's stable id: the device UUID, the lowercased email, or a Steam id.
        public string Subject { get; set; } = "";
        // Email provider only. Kept apart from Subject because it is PII.
        public string? Email { get; set; }
        // Argon2id encoded string, email provider only.
        public string? PwHash { get; set; }
        public DateTime LinkedAt { get; set; }
    }

    public class PlayerSession
    {
        public long Id { get; set; }
        public Guid PlayerId { get; set; }
        // SHA-256 of the session token; the raw token is never stored.
        public byte[] TokenHash { get; set; } = [];
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
