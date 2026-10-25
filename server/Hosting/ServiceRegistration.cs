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

            return services;
        }
    }
}
