/*!************************************************************************
\file   PlayerInputRules.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the rules for what a player may send as a display
        name or an email: length and character checks for the name, and
        trimming and a check plus lowercasing for the email. It also works
        out the key used to tell whether two Gmail addresses are the same
        inbox.

        Used by Register.cs, SetDisplayName.cs, CreatePlayer.cs and the
        email handlers. Lengths come from Limits.cs.
**************************************************************************/
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

        // the key two addresses are compared by, to tell if they reach the same inbox. gmail ignores dots
        // and anything after a + in the part before the @, and googlemail.com is the same inbox as
        // gmail.com, so Sher.Wyn+x@googlemail.com and sherwyn@gmail.com get the same key. every other
        // address is its own key, since other providers do treat dots and + as part of the name. takes an
        // address that NormalizeEmail already accepted. the address as typed is still what mail goes to
        internal static string EmailKey(string email)
        {
            var at = email.LastIndexOf('@');
            if (email[(at + 1)..] is not ("gmail.com" or "googlemail.com")) return email;

            var local = email[..at];
            var plus = local.IndexOf('+');
            if (plus >= 0) local = local[..plus];
            local = local.Replace(".", "");
            return local.Length == 0 ? email : $"{local}@gmail.com";
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
