using System.Net;

namespace server.Models
{
    public class AdminSession
    {
        public long Id { get; set; }
        public long AdminId { get; set; }
        // sha-256 of the session token, the raw token is never stored
        public byte[] TokenHash { get; set; } = [];
        public IPAddress? Ip { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        // pushed forward on each authenticated request but never past ExpiresAt. the session dies at
        // whichever limit (idle or absolute) comes first
        public DateTime IdleExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
