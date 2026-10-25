using Microsoft.AspNetCore.HttpOverrides;

namespace server.Hosting
{
    public static class ProxySetup
    {
        // behind caddy every request looks like it came from caddy, which breaks the per-ip rate limits
        // and the session ip. only caddy can reach the app in prod, so we trust its forwarded headers
        public static IServiceCollection AddProxyHeaders(this IServiceCollection services)
        {
            return services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }
    }
}
