namespace server.Models
{
    // One way a player can prove who they are. A player can have several (a device
    // plus an email, say) -- linking adds a row, it never replaces one.
    public class PlayerIdentity
    {
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public PlayerProvider Provider { get; set; }
        // The provider's stable id. For a device, the SHA-256 of the device token as lowercase
        // hex -- the token is a credential, so only its hash is kept. Otherwise the lowercased
        // email, or a Steam id.
        public string Subject { get; set; } = "";
        // Email provider only. Kept apart from Subject because it is PII.
        public string? Email { get; set; }
        // Argon2id encoded string, email provider only.
        public string? PwHash { get; set; }
        public DateTime LinkedAt { get; set; }
        // Email provider only: set once the player typed back the code mailed to them.
        // Null means the address is unproven, which also bars password reset.
        public DateTime? VerifiedAt { get; set; }
    }
}
