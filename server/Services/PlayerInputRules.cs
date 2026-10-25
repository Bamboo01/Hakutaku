using server.Constants;

namespace server.Services
{
    // what a player may send as a display name or an email
    internal static class PlayerInputRules
    {
        // a display name is just a label: not unique, never used to sign in. so the rules are only about
        // what can be shown: a length range once trimmed (playfab's limits) and no control characters,
        // which could break a ui or a log line. null means none was given, and that's fine.
        // whitespace on its own is too short
        internal static bool TryNormalizeDisplayName(string? raw, out string? displayName)
        {
            displayName = raw?.Trim();
            return displayName is null
                || (displayName.Length is >= Limits.DisplayNameMinLength and <= Limits.DisplayNameMaxLength && !displayName.Any(char.IsControl));
        }

        // trims and lowercases, then checks it looks like an email. only catches obvious typos,
        // it doesn't prove the address is real (the emailed code does that)
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
