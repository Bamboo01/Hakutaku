using System.Net;

namespace server.Models
{
    public enum AdminRole : short
    {
        Owner = 0,
        Admin = 1,
    }

    public class AdminUser
    {
        public long Id { get; set; }
        // The login identifier. Unique, case-insensitively.
        public string Username { get; set; } = "";
        // Optional -- not used for login, just a place to send report hooks later.
        public string? Email { get; set; }
        // Argon2id encoded string; the salt and parameters are inside it.
        public string PwHash { get; set; } = "";
        public AdminRole Role { get; set; }
        public byte[]? TotpSecret { get; set; }
        public DateTime CreatedAt { get; set; }
        public long? CreatedBy { get; set; }
        public DateTime? DisabledAt { get; set; }
    }

    public class AdminSession
    {
        public long Id { get; set; }
        public long AdminId { get; set; }
        // SHA-256 of the session token; the raw token is never stored.
        public byte[] TokenHash { get; set; } = [];
        public IPAddress? Ip { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        // Pushed forward on each authenticated request, capped at ExpiresAt. A session
        // dies at whichever of the two limits (idle or absolute) is hit first.
        public DateTime IdleExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
