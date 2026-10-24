namespace server.Models
{
    // A player account. How a player proves who they are lives in PlayerIdentity
    // (a device token, an email, ...), not on this row.
    public class Player
    {
        public Guid Id { get; set; }
        public int Xp { get; set; }
        // What the game shows for this player. Optional and not unique: it is a label, not
        // a way to sign in, so two players can both be "Bob". See PlayerInputRules.TryNormalizeDisplayName.
        public string? DisplayName { get; set; }
        // The hardware ID the game sent when this player registered (e.g. Unity's
        // SystemInfo.deviceUniqueIdentifier). For support and spotting reroll farms only:
        // it isn't secret, so nothing ever looks a player up or signs one in by it. Kept
        // here rather than on the device identity, which is deleted at email verification.
        public string? RegisteredHardwareId { get; set; }
    }
}
