using server.Constants;

namespace server.Endpoints.PlayerAuth
{
    public static class PlayerAuthRoutes
    {
        // The only file that knows the URLs and the rate limits: one line per route.
        // These are the public player routes, and each has its own handle block in
        // caddy/Caddyfile. Routes with no rate limit need a player token instead, and
        // check it themselves. Sessions are a Bearer token, not a cookie: the callers
        // are game clients, not browsers.
        public static void MapPlayerAuthRoutes(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/players");

            group.MapPost("/register", Register.Handle).RequireRateLimiting(RateLimits.PlayerRegister);
            group.MapPost("/login/device", LoginWithDevice.Handle).RequireRateLimiting(RateLimits.PlayerDeviceLogin);
            group.MapPost("/login/email", LoginWithEmail.Handle).RequireRateLimiting(RateLimits.PlayerLogin);

            group.MapPost("/display-name", SetDisplayName.Handle);
            group.MapPost("/link/email", LinkEmail.Handle);

            group.MapPost("/email/verify", VerifyEmail.Handle).RequireRateLimiting(RateLimits.PlayerMail);
            group.MapPost("/email/resend", ResendEmail.Handle).RequireRateLimiting(RateLimits.PlayerMail);
            group.MapPost("/password/forgot", ForgotPassword.Handle).RequireRateLimiting(RateLimits.PlayerMail);
            group.MapPost("/password/reset", ResetPassword.Handle).RequireRateLimiting(RateLimits.PlayerMail);
        }
    }
}
