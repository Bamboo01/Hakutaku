namespace server.Models
{
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
}
