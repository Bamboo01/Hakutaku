using server.Constants;

namespace server.Services
{
    // What a player is allowed to send as a display name or an email address.
    internal static class PlayerInputRules
    {
        // A display name is a label, not an identity: not unique and never used to sign in.
        // So the rules are only about what can be shown: a length range once trimmed
        // (PlayFab's limits) and no control characters, which could break a UI or a log
        // line. Null means none was given, which is allowed. Whitespace alone is too short.
        internal static bool TryNormalizeDisplayName(string? raw, out string? displayName)
        {
            displayName = raw?.Trim();
            return displayName is null
                || (displayName.Length is >= Limits.DisplayNameMinLength and <= Limits.DisplayNameMaxLength && !displayName.Any(char.IsControl));
        }

        // Trims and lowercases, then checks the address looks like one. This only stops
        // obvious typos. It doesn't prove the address is real; the emailed code does that.
        internal static string? NormalizeEmail(string? raw)
        {
            var email = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(email) || email.Length > Limits.MaxIdentifierLength) return null;
            var at = email.IndexOf('@');
            if (at < 1 || at != email.LastIndexOf('@') || at == email.Length - 1) return null;
            return email.Any(char.IsWhiteSpace) ? null : email;
        }
    }
}
