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
    }
}
