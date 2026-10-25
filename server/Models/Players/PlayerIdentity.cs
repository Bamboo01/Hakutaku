namespace server.Models
{
    // one way a player can prove who they are. a player can have several (device + email, say),
    // linking adds a row and never replaces one
    public class PlayerIdentity
    {
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public PlayerProvider Provider { get; set; }
        // the provider's stable id. device: sha-256 of the device token as lowercase hex (the token is a
        // credential so only its hash is kept). otherwise the lowercased email, or a steam id
        public string Subject { get; set; } = "";
        // email only. kept apart from Subject because it's pii
        public string? Email { get; set; }
        // argon2id string, email only
        public string? PwHash { get; set; }
        public DateTime LinkedAt { get; set; }
        // email only: set once the player types back the mailed code. null means the address is
        // unproven, which also blocks password reset
        public DateTime? VerifiedAt { get; set; }
    }
}
