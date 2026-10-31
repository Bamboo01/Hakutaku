/*!************************************************************************
\file   ServiceRegistration.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines AddAppServices, which registers everything that
        touches the database, the clock or configuration. Most are made once
        per request and share that request's Db. The clock and the
        link-attempt notifier are singletons.

        Called from Program.cs. Handlers take these services as parameters.
**************************************************************************/
using server.Auth;
using server.Services;

namespace server.Hosting
{
    public static class ServiceRegistration
    {
        // everything that touches the db, the clock or config is a service, made once per request
        // (they share that request's Db). the clock is a singleton so tests can swap in a fake one
        public static IServiceCollection AddAppServices(this IServiceCollection services)
        {
            services.AddSingleton(TimeProvider.System);

            services.AddScoped<AdminSessionService>();
            services.AddScoped<PlayerSessionService>();
            services.AddScoped<PlayerIdentityService>();
            services.AddScoped<EmailCodeService>();
            services.AddScoped<OwnerSeeder>();

            // remembers which emails already got a "someone tried to use your email" heads-up, and how
            // often each player changed their display name
            services.AddMemoryCache();
            services.AddSingleton<LinkAttemptNotifier>();
            services.AddSingleton<DisplayNameLimiter>();

            return services;
        }
    }
}
