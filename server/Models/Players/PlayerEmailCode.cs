namespace server.Models
{
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
}
