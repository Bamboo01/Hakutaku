/*!************************************************************************
\file   PlayerDataRules.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the rules for what may be sent as player data: how
        a tier is named, what a key looks like, what a value may hold, and
        how a list of keys in a URL is read. They are plain checks with no
        database, so they are easy to test on their own.

        Used by GetPlayerData.cs and UpdatePlayerData.cs. The numbers come
        from Limits.cs.
**************************************************************************/
using System.Text;
using server.Constants;
using server.Models;

namespace server.Services
{
    internal static class PlayerDataRules
    {
        // a tier is given by its name, not its number, and the case doesn't matter. "1" is refused on purpose:
        // names say what they mean, and they can't be mixed up if the numbers ever change
        internal static bool TryParseTier(string? raw, out PlayerDataTier tier)
        {
            tier = default;
            if (raw is null) return false;
            foreach (var name in Enum.GetNames<PlayerDataTier>())
            {
                if (!string.Equals(raw, name, StringComparison.OrdinalIgnoreCase)) continue;
                tier = Enum.Parse<PlayerDataTier>(name);
                return true;
            }
            return false;
        }

        // letters, digits and _ . - only, 1 to 64 of them. written as a loop and not a regex, because a regex
        // $ also matches before a trailing newline, so "Xp\n" would sneak through
        internal static bool IsValidKey(string? key) =>
            key is { Length: >= 1 and <= Limits.PlayerDataKeyMaxLength }
            && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

        // text up to 16 KB. the cap is bytes (not characters) since that's what gets stored. a null character
        // is refused because postgres text can't hold one, and it would turn into a server error
        internal static bool IsValidValue(string? value) =>
            value is not null && !value.Contains('\0') && Encoding.UTF8.GetByteCount(value) <= Limits.PlayerDataValueMaxBytes;

        // reads "Xp,Foo" from the keys part of the url. every one must be a valid key, so a blank entry
        // ("Xp,,Foo" or an empty keys=) is refused and never quietly treated as "all of them". repeats are dropped
        internal static bool TryParseKeys(string raw, out List<string> keys)
        {
            keys = [];
            foreach (var part in raw.Split(','))
            {
                var key = part.Trim();
                if (!IsValidKey(key)) return false;
                if (!keys.Contains(key)) keys.Add(key);
            }
            return true;
        }
    }
}
