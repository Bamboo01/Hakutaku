/*!************************************************************************
\file   Limits.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the size limits on what a client can send:
        passwords, display names, identifiers, character names and event
        payloads.

        Checked by the handlers and quoted in ErrorMessages.cs.
**************************************************************************/
namespace server.Constants
{
    // size limits on what a client can send. messages that quote a limit are built from these
    // (see ErrorMessages) so a message can't drift from the check
    public static class Limits
    {
        // longest email address that can exist (rfc 5321). usernames, hardware ids and device tokens
        // share it so no identifying field can be huge
        public const int MaxIdentifierLength = 254;

        // admin and player passwords. the max stops an argon2 hash being fed a giant string
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 256;

        // playfab's limits for a display name
        public const int DisplayNameMinLength = 3;
        public const int DisplayNameMaxLength = 25;

        // character names and telemetry events. the data cap is bytes (not characters) since that's
        // what actually gets stored, and the whole list endpoint returns every row
        public const int CharacterNameMinLength = 1;
        public const int CharacterNameMaxLength = 50;
        public const int EventTypeMaxLength = 100;
        public const int EventDataMaxBytes = 10_000;

        // what the owner calls a server key, e.g. "homelab"
        public const int ServerKeyNameMaxLength = 100;

        // player data, which a game server reads and writes. a value is text, and the cap is bytes (not
        // characters) since that's what gets stored. the per-request cap stops one call doing a lot of
        // writes, and the per-tier cap stops one player collecting keys forever
        public const int PlayerDataKeyMaxLength = 64;
        public const int PlayerDataValueMaxBytes = 16 * 1024;
        public const int PlayerDataMaxKeysPerTier = 100;
        public const int PlayerDataMaxChangesPerRequest = 10;

        // a session ticket is 43 characters (32 random bytes as base64url). anything much longer can't be
        // one, so it's refused before it is hashed
        public const int SessionTicketMaxLength = 512;
    }
}
