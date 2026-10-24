namespace server.Constants
{
    // Size limits on what a client can send. Error messages that quote a limit are built
    // from these (see ErrorMessages), so a message can't drift from the check it describes.
    public static class Limits
    {
        // The longest email address that can exist (RFC 5321). Usernames, hardware IDs and
        // device tokens share the cap so no identifying field can be huge.
        public const int MaxIdentifierLength = 254;

        // Admin and player passwords. The top end keeps an Argon2 hash from being fed a
        // giant string.
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 256;

        // PlayFab's limits for a display name.
        public const int DisplayNameMinLength = 3;
        public const int DisplayNameMaxLength = 25;
    }
}
