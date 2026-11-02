/*!************************************************************************
\file   ErrorMessages.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the error messages sent by more than one route,
        plus the ones that quote a limit. Those are built from Limits so a
        message cannot drift from its check. Clients might compare these
        strings, so changing one is a visible change.

        Built from Limits.cs. Used by the handlers and by ApiErrors.cs.
**************************************************************************/
using server.Models;

namespace server.Constants
{
    // messages sent by more than one route, plus the ones that quote a limit. a message only used once
    // stays next to its route. clients might compare these strings, so changing one is a visible change
    public static class ErrorMessages
    {
        public const string NotLoggedIn = "not logged in";

        // same for a wrong password and an unknown account, so it never says which
        public const string InvalidCredentials = "invalid credentials";

        public const string InvalidOrExpiredCode = "invalid or expired code";
        public const string NoEmailLinked = "no email is linked to this player";
        public const string EmailAlreadyLinked = "this player already has an email linked";

        // for unknown /api paths and any unexpected exception. the 500 text is vague on purpose,
        // details go to the log, never to the client
        public const string RouteNotFound = "no such route";
        public const string ServerError = "something went wrong on the server";

        // these quote a limit, so they're built from Limits
        static readonly string PasswordRange = $"{Limits.MinPasswordLength}-{Limits.MaxPasswordLength} characters";

        public static readonly string DisplayNameRule =
            $"displayName must be {Limits.DisplayNameMinLength}-{Limits.DisplayNameMaxLength} characters, with no control characters";

        public static readonly string DisplayNameRateLimited =
            $"display name can only be changed {RateLimits.DisplayNameChanges} times an hour, try again later";

        public static readonly string TooManyCodesToday =
            $"too many codes today (up to {RateLimits.CodesPerDay} a day), try again tomorrow";

        public static readonly string CreateServerKeyInvalid =
            $"name is required ({Limits.ServerKeyNameMaxLength} characters at most, no control characters) and expiresAt, if given, must be in the future";

        // player data. the tier names come from the enum so the message can't drift from it
        public static readonly string PlayerDataTierInvalid =
            $"tier must be {nameof(PlayerDataTier.ClientWritable)}, {nameof(PlayerDataTier.ReadOnly)} or {nameof(PlayerDataTier.Internal)}";

        public static readonly string PlayerDataKeyInvalid =
            $"keys must be 1-{Limits.PlayerDataKeyMaxLength} characters, using only letters, digits, _ . and -";

        public static readonly string PlayerDataValueInvalid =
            $"values must be text of at most {Limits.PlayerDataValueMaxBytes / 1024} KB, with no null characters";

        public static readonly string PlayerDataTooManyChanges =
            $"at most {Limits.PlayerDataMaxChangesPerRequest} keys can be changed in one request";

        public static readonly string PlayerDataTooManyKeys =
            $"a player can have at most {Limits.PlayerDataMaxKeysPerTier} keys in a tier";

        public const string PlayerDataKeyTwice = "a key can't be both set and removed in the same request";

        public static readonly string HardwareIdRequired =
            $"hardwareId is required (1-{Limits.MaxIdentifierLength} characters); register no longer takes a deviceId";

        public static readonly string CreateAdminInvalid =
            $"username is required and password must be {PasswordRange}";

        public static readonly string LinkEmailInvalid =
            $"a valid email is required and the password must be {PasswordRange}";

        public static readonly string ResetPasswordInvalid =
            $"a valid email is required and the new password must be {PasswordRange}";

        // a playerId that isn't a real player, for characters and events
        public const string NoSuchPlayer = "no such player";

        public static readonly string CreateCharacterInvalid =
            $"playerId is required and name must be {Limits.CharacterNameMinLength}-{Limits.CharacterNameMaxLength} characters";

        public static readonly string CreateEventInvalid =
            $"playerId is required and eventType must be 1-{Limits.EventTypeMaxLength} characters";

        public static readonly string EventDataInvalid =
            $"data must be valid json, up to {Limits.EventDataMaxBytes} bytes";
    }
}
