namespace server.Constants
{
    public static class RateLimits
    {
        // Policy names. Each is used twice: where the policy is created (Program.cs) and
        // on the routes it limits (RequireRateLimiting). A group of routes has its own
        // policy so one group can't use up another's allowance.
        public const string AdminLogin = "admin-login";

        // Public and unauthenticated, so this is the only thing standing between
        // registration and someone hammering it to fill the table.
        public const string PlayerRegister = "player-register";

        // Email login is public too, and each attempt costs an Argon2 hash.
        public const string PlayerLogin = "player-login";

        // Device login. A device token can't be guessed, so this only caps load.
        public const string PlayerDeviceLogin = "player-device-login";

        // The mail routes (verify, resend, forgot, reset). Each one either sends an email
        // or lets someone guess a code, so they share one limit.
        public const string PlayerMail = "player-mail";

        // Every policy allows this many requests per client IP in each window.
        public const int PermitLimit = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        // What a request with no known IP is counted under, so those all share one allowance.
        public const string UnknownClient = "unknown";
    }
}
