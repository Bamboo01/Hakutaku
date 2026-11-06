#nullable enable

using System;
using System.Collections.Generic;

namespace Hakutaku
{
    // The server's routes and JSON shapes, kept in one place. The public models in
    // ClientModels.cs are the SDK's own PlayFab-style types and are mapped from these,
    // so a server-side change (the planned /api/players -> /api/player rename, say) is
    // an edit here and not a breaking change for every game using the SDK.
    static class Routes
    {
        public const string Register = "/api/players/register";
        public const string LoginDevice = "/api/players/login/device";
        public const string LoginEmail = "/api/players/login/email";
        public const string LinkEmail = "/api/players/link/email";
        public const string DisplayName = "/api/players/display-name";
        public const string VerifyEmail = "/api/players/email/verify";
        public const string ResendEmail = "/api/players/email/resend";
        public const string ForgotPassword = "/api/players/password/forgot";
        public const string ResetPassword = "/api/players/password/reset";
        public const string Events = "/api/events";
        public const string Characters = "/api/characters";

        // The game server's routes, called by the server SDK (sdk-server/), which compiles this file in
        // too. They need a server key, never a session.
        public const string ValidateSession = "/api/server/sessions/validate";
        public static string PlayerData(string playerId, string tier) =>
            "/api/server/players/" + Uri.EscapeDataString(playerId) + "/data?tier=" + tier;
    }

    // The code the validate route sends with a 401 for a bad ticket. A 401 without it means the
    // server key itself was refused.
    static class ServerCodes
    {
        public const string InvalidTicket = "invalid_ticket";
    }

    // What register and both logins answer with. DeviceToken comes only from register.
    class SessionWire
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? DeviceToken { get; set; }
        public string? Token { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    class LinkEmailWire
    {
        public string? Email { get; set; }
        public bool Verified { get; set; }
        public bool VerificationSent { get; set; }
    }

    class DisplayNameWire
    {
        public string? DisplayName { get; set; }
    }

    class VerifiedWire
    {
        public bool Verified { get; set; }
        public bool DeviceTokenRemoved { get; set; }
    }

    class SentWire
    {
        public bool Sent { get; set; }
    }

    class OkWire
    {
        public bool Ok { get; set; }
    }

    // POST /api/events echoes the whole stored row; only the id is of use.
    class EventWire
    {
        public string? Id { get; set; }
    }

    class CharacterWire
    {
        public string? Id { get; set; }
        public string? PlayerId { get; set; }
        public string? Name { get; set; }
    }

    // ---- The game server's routes (server SDK only)

    class ValidateSessionWire
    {
        public string? PlayerId { get; set; }
        public string? DisplayName { get; set; }
    }

    class PlayerDataWire
    {
        public Dictionary<string, string>? Data { get; set; }
    }

    class PlayerDataUpdatedWire
    {
        public int Updated { get; set; }
    }
}
