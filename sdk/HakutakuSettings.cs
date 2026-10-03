#nullable enable

using System;

namespace Hakutaku
{
    // Where requests go. PlayFab builds this from a TitleId; Hakutaku is self-hosted and
    // serves a single game, so there is no title, only the server's base URL.
    public class HakutakuApiSettings
    {
        public string ServerUrl = "http://localhost:5008";
    }

    // The session of one player, like PlayFabAuthenticationContext. Each client instance
    // has its own, so one process (the simulator, a dedicated server) can hold several
    // players at once without their sessions overwriting each other.
    public class HakutakuAuthenticationContext
    {
        // Opaque: never parse it or assume it is a GUID (see docs/components/sdk.md).
        public string? PlayerId;
        // The Bearer token from register or login. Only its hash is stored server-side,
        // so it can't be fetched again; logging in again issues a new one.
        public string? SessionTicket;
        public DateTime? SessionExpiration;

        public bool IsClientLoggedIn() =>
            !string.IsNullOrEmpty(SessionTicket) && (SessionExpiration == null || SessionExpiration > DateTime.UtcNow);

        // Client-side only. There is no player logout endpoint yet, so the token itself
        // stays valid on the server until it expires or a password reset revokes it.
        public void ForgetAllCredentials()
        {
            PlayerId = null;
            SessionTicket = null;
            SessionExpiration = null;
        }
    }

    // What the static HakutakuClientAPI uses, named like PlayFabSettings' fields.
    public static class HakutakuSettings
    {
        public static readonly HakutakuApiSettings staticSettings = new HakutakuApiSettings();
        public static readonly HakutakuAuthenticationContext staticPlayer = new HakutakuAuthenticationContext();
    }
}
