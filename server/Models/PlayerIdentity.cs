namespace server.Models
{
    public enum PlayerProvider : short
    {
        // A guest's device token, minted by register. Deleted once the player verifies an
        // email, since the account is recoverable from then on.
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

    public enum EmailCodePurpose : short
    {
        Verify = 0,
        Reset = 1,
    }

    // A short code mailed to a player. Single use, short lived, and limited to a few
    // wrong guesses, because six digits alone are easy to brute-force.
    public class PlayerEmailCode
    {
        public long Id { get; set; }
        public Guid PlayerId { get; set; }
        public EmailCodePurpose Purpose { get; set; }
        // SHA-256 of the code, tied to the player and purpose. The raw code is only ever mailed.
        public byte[] CodeHash { get; set; } = [];
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int Attempts { get; set; }
        public DateTime? UsedAt { get; set; }
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
