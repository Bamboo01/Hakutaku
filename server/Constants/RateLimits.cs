namespace server.Constants
{
    public static class RateLimits
    {
        // policy names. each is used twice: where the policy is made (RateLimiting.cs) and on the routes it
        // limits. a group of routes gets its own policy so one group can't use up another's allowance
        public const string AdminLogin = "admin-login";

        // public and unauthenticated, so this is all that stands between registration and someone
        // spamming it to fill the table
        public const string PlayerRegister = "player-register";

        // email login is public too, and every attempt costs an argon2 hash
        public const string PlayerLogin = "player-login";

        // device login. a device token can't be guessed, so this only caps load
        public const string PlayerDeviceLogin = "player-device-login";

        // the mail routes (verify, resend, forgot, reset). each one sends an email or lets someone
        // guess a code, so they share one limit
        public const string PlayerMail = "player-mail";

        // link email hashes a password and can send mail, and a session is free to get, so it is limited too
        public const string PlayerLink = "player-link";

        // every policy allows this many requests per client ip in each window
        public const int PermitLimit = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        // what a request with no known ip is counted under, so they all share one allowance
        public const string UnknownClient = "unknown";
    }
}
