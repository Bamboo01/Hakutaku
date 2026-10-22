namespace server.Constants
{
    // The messages that more than one route sends. A message used in only one place stays
    // next to its route. Clients may compare these strings, so changing one is a visible change.
    public static class ErrorMessages
    {
        public const string NotLoggedIn = "not logged in";

        // The same for a wrong password and an unknown account, so it never says which.
        public const string InvalidCredentials = "invalid credentials";

        public const string InvalidOrExpiredCode = "invalid or expired code";
        public const string NoEmailLinked = "no email is linked to this player";
        public const string EmailAlreadyLinked = "this player already has an email linked";
        public const string EmailInUse = "email already in use";
    }
}
