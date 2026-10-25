namespace server.Models
{
    public class PlayerSession
    {
        public long Id { get; set; }
        public Guid PlayerId { get; set; }
        // sha-256 of the session token, the raw token is never stored
        public byte[] TokenHash { get; set; } = [];
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
