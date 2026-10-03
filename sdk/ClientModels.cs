#nullable enable

using System;
using System.Collections.Generic;

// Request and result types for HakutakuClientAPI. They are named after their PlayFab
// counterparts wherever one exists, and every call takes a request object even when it
// has no fields, also like PlayFab, so fields can be added later without breaking callers.
namespace Hakutaku.ClientModels
{
    // POST /api/players/register. Makes a new guest player every time, and the result's
    // DeviceToken is that guest's credential: store it, and sign in with
    // LoginWithDeviceToken on later launches.
    public class RegisterGuestRequest
    {
        // An identifier of the hardware, such as Unity's SystemInfo.deviceUniqueIdentifier.
        // Required, but recorded only for support and abuse tracking: it isn't secret, so
        // the server never signs anyone in with it. 1-254 characters.
        public string? HardwareId;
        // Optional: 3-25 characters, and not unique. UpdateUserTitleDisplayName changes it.
        public string? DisplayName;
    }

    // POST /api/players/login/device. Never creates a player: an unknown token fails with
    // NotAuthenticated, and the game should offer "new game" or "I have an account".
    public class LoginWithDeviceTokenRequest
    {
        public string? DeviceToken;
    }

    // POST /api/players/login/email. Needs an email linked with LinkEmailAddress first.
    public class LoginWithEmailAddressRequest
    {
        public string? Email;
        public string? Password;
    }

    public class LoginResult
    {
        public string PlayerId = "";
        // Null until the player has one.
        public string? DisplayName;
        public string SessionTicket = "";
        public DateTime SessionExpiration;
        public int Xp;
        // Only from RegisterGuest, and only that once: the server keeps just its hash. Store
        // it like a password, e.g. in the iOS Keychain or Android Keystore.
        public string? DeviceToken;
    }

    // POST /api/players/display-name. Sets or replaces the name the game shows for this
    // player: 3-25 characters, no control characters, and not unique, so it is a label
    // and must never be used to identify anyone.
    public class UpdateUserTitleDisplayNameRequest
    {
        public string? DisplayName;
    }

    public class UpdateUserTitleDisplayNameResult
    {
        // As stored, i.e. trimmed.
        public string DisplayName = "";
    }

    // POST /api/players/link/email, PlayFab's AddUsernamePassword. Adds an email and
    // password to a guest. The server mails a 6-digit code to prove the address, which
    // VerifyEmail redeems; the device token keeps working until then.
    public class LinkEmailAddressRequest
    {
        public string? Email;
        // 8-256 characters.
        public string? Password;
    }

    public class LinkEmailAddressResult
    {
        public string Email = "";
        public bool Verified;
        // False if the mail couldn't be sent; ResendVerificationEmail asks for another.
        public bool VerificationSent;
    }

    // POST /api/players/email/verify
    public class VerifyEmailRequest
    {
        public string? Code;
    }

    public class VerifyEmailResult
    {
        public bool Verified;
        // True when verifying deleted the player's device token on the server. Delete the
        // stored copy too; from now on the player signs in by email. The current session
        // keeps working.
        public bool DeviceTokenRemoved;
    }

    // POST /api/players/email/resend. At most one code a minute per player.
    public class ResendVerificationEmailRequest
    {
    }

    public class ResendVerificationEmailResult
    {
        public bool Sent;
    }

    // POST /api/players/password/forgot. Only a verified email gets a code, but the
    // answer is the same either way so it can't be used to look up accounts.
    public class SendAccountRecoveryEmailRequest
    {
        public string? Email;
    }

    public class SendAccountRecoveryEmailResult
    {
    }

    // POST /api/players/password/reset. Revokes every session the player has, including
    // the one this client holds, so log in again afterwards.
    public class ResetPasswordRequest
    {
        public string? Email;
        public string? Code;
        // 8-256 characters.
        public string? NewPassword;
    }

    public class ResetPasswordResult
    {
    }

    // POST /api/events. The server stores Body as a JSON string and stamps its own
    // timestamp, so there is no Timestamp field to set.
    public class WriteClientPlayerEventRequest
    {
        public string? EventName;
        public Dictionary<string, object>? Body;
    }

    public class WriteEventResponse
    {
        public string EventId = "";
    }

    // GET /api/characters
    public class ListUsersCharactersRequest
    {
    }

    public class ListUsersCharactersResult
    {
        public List<CharacterResult> Characters = new List<CharacterResult>();
    }

    public class CharacterResult
    {
        public string CharacterId = "";
        public string CharacterName = "";
    }

    // POST /api/characters. PlayFab's version takes a catalog ItemId; Hakutaku has no
    // catalog, so a name is all a character needs.
    public class GrantCharacterToUserRequest
    {
        public string? CharacterName;
    }

    public class GrantCharacterToUserResult
    {
        public string CharacterId = "";
    }
}
