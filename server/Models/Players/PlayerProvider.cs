namespace server.Models
{
    public enum PlayerProvider : short
    {
        // a guest's device token, made by register. deleted once they verify an email, since the
        // account is recoverable from then on
        Device = 0,
        Email = 1,
        Steam = 2,
    }
}
