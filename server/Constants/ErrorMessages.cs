namespace server.Constants
{
    // The messages that more than one route sends, plus the ones that quote a limit.
    // A message used in only one place otherwise stays next to its route. Clients may
    // compare these strings, so changing one is a visible change.
    public static class ErrorMessages
    {
        public const string NotLoggedIn = "not logged in";

        // The same for a wrong password and an unknown account, so it never says which.
        public const string InvalidCredentials = "invalid credentials";

        public const string InvalidOrExpiredCode = "invalid or expired code";
        public const string NoEmailLinked = "no email is linked to this player";
        public const string EmailAlreadyLinked = "this player already has an email linked";
        public const string EmailInUse = "email already in use";

        // These quote a limit, so they are built from Limits.
        static readonly string PasswordRange = $"{Limits.MinPasswordLength}-{Limits.MaxPasswordLength} characters";

        public static readonly string DisplayNameRule =
            $"displayName must be {Limits.DisplayNameMinLength}-{Limits.DisplayNameMaxLength} characters, with no control characters";

        public static readonly string HardwareIdRequired =
            $"hardwareId is required (1-{Limits.MaxIdentifierLength} characters); register no longer takes a deviceId";

        public static readonly string CreateAdminInvalid =
            $"username is required and password must be {PasswordRange}";

        public static readonly string LinkEmailInvalid =
            $"a valid email is required and the password must be {PasswordRange}";

        public static readonly string ResetPasswordInvalid =
            $"a valid email is required and the new password must be {PasswordRange}";
    }
}
