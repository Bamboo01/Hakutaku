/*!************************************************************************
\file   RateLimits.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the names of the rate limit policies (admin login,
        player register, device login, email login, mail and link email).
        Each name is used twice: where the policy is created and on the
        routes it limits.

        Policies are created in Hosting/RateLimiting.cs and applied in
        AdminRoutes.cs and PlayerAuthRoutes.cs.
**************************************************************************/
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

        // display name changes are limited per player instead of per ip: DisplayNameLimiter counts them
        // once the token is checked, so players on one shared ip don't use up each other's changes
        public const int DisplayNameChanges = 5;
        public static readonly TimeSpan DisplayNameWindow = TimeSpan.FromHours(1);

        // codes mailed to one account (per purpose) in 24 hours, on top of the one-a-minute gap. without
        // it, forgot password could be used to flood someone's inbox, one mail a minute, all day
        public const int CodesPerDay = 5;

        // every policy allows this many requests per client ip in each window
        public const int PermitLimit = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        // what a request with no known ip is counted under, so they all share one allowance
        public const string UnknownClient = "unknown";
    }
}
