// Ported from Sundered Frames' SFPlayerXp.cs (Gam300, feature/hakutaku-integration).
using Mirror;

namespace HakutakuMirrorSample
{
    // The way for server gameplay code to give a player XP. What earns XP, and how much, is up to the caller:
    //
    //     SamplePlayerXp.Award(conn, amount);
    //
    // An online player's new total is saved to Hakutaku in the background. An offline player's XP only lasts for
    // this connection, because their real total is unknown and must not be overwritten.
    public static class SamplePlayerXp
    {
        // Set by SampleAuthenticator while the server is running
        internal static SampleXpSaver Saver;

        // False if amount isn't positive or the connection hasn't been authenticated
        public static bool Award(NetworkConnectionToClient conn, long amount)
        {
            if (amount <= 0 || conn == null || !(conn.authenticationData is SamplePlayerAuthData data))
                return false;

            data.Xp = amount > long.MaxValue - data.Xp ? long.MaxValue : data.Xp + amount;

            if (data.IsOnline)
                Saver?.SetTotal(data.PlayerId, data.Xp);

            return true;
        }

        public static long GetTotal(NetworkConnectionToClient conn) =>
            conn?.authenticationData is SamplePlayerAuthData data ? data.Xp : 0;
    }
}
