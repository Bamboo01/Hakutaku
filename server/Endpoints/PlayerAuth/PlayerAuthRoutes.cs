/*!************************************************************************
\file   PlayerAuthRoutes.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines MapPlayerAuthRoutes, which maps the public player
        routes under /api/players (register, both logins, link email,
        verify, resend, password reset and display name) and their rate
        limits. Sessions are a Bearer token and not a cookie, since the
        callers are game clients.

        Called from Program.cs. Each route has its own handle block in
        caddy/Caddyfile. Policy names come from RateLimits.cs.
**************************************************************************/
using server.Constants;

namespace server.Endpoints.PlayerAuth
{
    public static class PlayerAuthRoutes
    {
        // only file that knows the urls and rate limits, one line per route. these are the public player
        // routes, each has its own handle block in caddy/Caddyfile. routes without a rate limit need a
        // player token and check it themselves. sessions are a bearer token, not a cookie, since the
        // callers are game clients and not browsers
        public static void MapPlayerAuthRoutes(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/players");

            group.MapPost("/register", Register.Handle).RequireRateLimiting(RateLimits.PlayerRegister);
            group.MapPost("/login/device", LoginWithDevice.Handle).RequireRateLimiting(RateLimits.PlayerDeviceLogin);
            group.MapPost("/login/email", LoginWithEmail.Handle).RequireRateLimiting(RateLimits.PlayerLogin);

            group.MapPost("/display-name", SetDisplayName.Handle);
            group.MapPost("/link/email", LinkEmail.Handle).RequireRateLimiting(RateLimits.PlayerLink);

            group.MapPost("/email/verify", VerifyEmail.Handle).RequireRateLimiting(RateLimits.PlayerMail);
            group.MapPost("/email/resend", ResendEmail.Handle).RequireRateLimiting(RateLimits.PlayerMail);
            group.MapPost("/password/forgot", ForgotPassword.Handle).RequireRateLimiting(RateLimits.PlayerMail);
            group.MapPost("/password/reset", ResetPassword.Handle).RequireRateLimiting(RateLimits.PlayerMail);
        }
    }
}
