// Ported from Sundered Frames' SFPlayerAuthData.cs (Gam300, feature/hakutaku-integration).
namespace HakutakuMirrorSample
{
    // Who a connection is, on conn.authenticationData once the authenticator has accepted it. Server only.
    public sealed class SamplePlayerAuthData
    {
        // Hakutaku's id for the player. Null if they joined without a valid session.
        public string PlayerId;

        // Can be empty: display names are optional in Hakutaku
        public string DisplayName;

        // The player's total in memory
        public long Xp;

        // True only after XP was really read from Hakutaku. While false, Xp is NOT the player's total, because a
        // failed read isn't 0 XP, so it must never be written back.
        public bool IsOnline;
    }
}
