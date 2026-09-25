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
        public string Email { get; set; } = "";
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
        public DateTime? RevokedAt { get; set; }
    }
}
