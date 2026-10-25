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
        public const string EmailInUse = "email already in use";

        // for unknown /api paths and any unexpected exception. the 500 text is vague on purpose,
        // details go to the log, never to the client
        public const string RouteNotFound = "no such route";
        public const string ServerError = "something went wrong on the server";

        // these quote a limit, so they're built from Limits
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
