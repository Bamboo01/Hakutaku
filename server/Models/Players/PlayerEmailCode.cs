namespace server.Models
{
    // a short code mailed to a player. single use, short lived, and only a few wrong guesses
    // allowed, because six digits alone are easy to brute-force
    public class PlayerEmailCode
    {
        public long Id { get; set; }
        public Guid PlayerId { get; set; }
        public EmailCodePurpose Purpose { get; set; }
        // sha-256 of the code, tied to the player and purpose. the raw code is only ever mailed
        public byte[] CodeHash { get; set; } = [];
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int Attempts { get; set; }
        public DateTime? UsedAt { get; set; }
    }
}
