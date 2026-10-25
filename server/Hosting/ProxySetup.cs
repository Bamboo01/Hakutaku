/*!************************************************************************
\file   ProxySetup.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines AddProxyHeaders, which makes the app trust the
        X-Forwarded-For and X-Forwarded-Proto headers. Behind Caddy every
        request would otherwise look like it came from Caddy, which would
        break the per-IP rate limits and the session IP.

        Called from Program.cs. The headers are applied by
        UseForwardedHeaders there.
**************************************************************************/
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
