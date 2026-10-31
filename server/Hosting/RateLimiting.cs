/*!************************************************************************
\file   RateLimiting.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines AddAppRateLimiting, which creates the rate limit
        policies that routes name with RequireRateLimiting. Every policy is
        the same rule, so many requests per client IP per window, but each
        group of routes has its own so one cannot use up another's
        allowance.

        Policy names are in RateLimits.cs. Called from Program.cs.
**************************************************************************/
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using server.Constants;

namespace server.Hosting
{
    public static class RateLimiting
    {
        // sets up the rate limit policies that routes name in RequireRateLimiting(...)
        public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
        {
            return services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // every policy is the same rule: so many requests per client ip per window. they're separate
                // policies so each group of routes gets its own allowance (see RateLimits)
                void AddPerIpPolicy(string name) => o.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? RateLimits.UnknownClient,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = RateLimits.PermitLimit, Window = RateLimits.Window }));

                AddPerIpPolicy(RateLimits.AdminLogin);
                AddPerIpPolicy(RateLimits.PlayerRegister);
                AddPerIpPolicy(RateLimits.PlayerLogin);
                AddPerIpPolicy(RateLimits.PlayerDeviceLogin);
                AddPerIpPolicy(RateLimits.PlayerMail);
                AddPerIpPolicy(RateLimits.PlayerLink);
            });
        }
    }
}
