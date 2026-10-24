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
}
