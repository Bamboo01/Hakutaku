using System.Net;

namespace server.Models
{
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
