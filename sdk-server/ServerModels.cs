#nullable enable

using System.Collections.Generic;

// The server SDK's request and result types, named after PlayFab's server API
// (PlayFab.ServerModels). They are mapped from the wire shapes in ServerContract.cs, so a change on
// the server is an edit there, not a breaking change for every game server.
namespace Hakutaku.ServerModels
{
    public class AuthenticateSessionTicketRequest
    {
        // The SessionTicket the player's client got from a login, sent to the game server when it joins.
        public string SessionTicket = "";
    }

    public class AuthenticateSessionTicketResult
    {
        public string PlayerId = "";
        public string? DisplayName;
    }

    // For GetUserReadOnlyData. PlayFab calls it GetUserDataRequest too.
    public class GetUserDataRequest
    {
        public string PlayerId = "";
        // The keys wanted. Null or empty means every key. A key the player doesn't have is left out of
        // the result, so treat a missing key as "not set yet".
        public List<string>? Keys;
    }

    public class GetUserDataResult
    {
        public string PlayerId = "";
        // Values are plain text, as in PlayFab. A number like XP comes back as "1200".
        public Dictionary<string, string> Data = new Dictionary<string, string>();
    }

    // For UpdateUserReadOnlyData. A write sets values and never adds to them, so retrying one is harmless.
    // At most 10 changes (sets plus removals) per call, and a key can't be both set and removed.
    public class UpdateUserDataRequest
    {
        public string PlayerId = "";
        public Dictionary<string, string>? Data;
        public List<string>? KeysToRemove;
    }

    public class UpdateUserDataResult
    {
        // How many keys were set, plus how many existing keys were removed.
        public int Updated;
    }
}
