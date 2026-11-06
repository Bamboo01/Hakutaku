/*!************************************************************************
\file   ServerKeyGuards.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines RequireServerKey, an extension that is chained onto
        a route or a route group so only a game server with a valid server
        key can call it. It also holds the name of the header the key is
        sent in and the name of the request item the key's id is put under.

        Used by GameServerRoutes.cs. The check itself is in
        RequireServerKeyFilter.cs, and AdminGuards.cs is the same idea for
        admin sessions.
**************************************************************************/
namespace server.Auth
{
    public static class ServerKeyGuards
    {
        // the header a game server sends its key in
        public const string HeaderName = "X-Server-Key";

        // where the filter leaves the id of the key that was used, in HttpContext.Items
        public const string ItemKey = "ServerKeyId";

        // chain it on a route or a group like .RequireAdmin(): app.MapGroup("/api/server").RequireServerKey().
        // a server key only opens routes that ask for it. the admin routes read the cookie and nothing
        // else, so a server key never works on /api/admin/*
        public static TBuilder RequireServerKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        {
            builder.AddEndpointFilter<TBuilder, RequireServerKeyFilter>();
            return builder;
        }
    }
}
