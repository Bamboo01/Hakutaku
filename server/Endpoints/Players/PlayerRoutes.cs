namespace server.Endpoints.Players
{
    public static class PlayerRoutes
    {
        // The admin's view of players. The public sign-in routes under /api/players/...
        // are separate (PlayerAuth.cs), and a player's own token doesn't unlock these yet.
        public static void MapPlayerRoutes(this IEndpointRouteBuilder app)
        {
            // Admin-only, so the check goes on the whole group instead of each route.
            var group = app.MapGroup("/api/players").RequireAdmin();

            group.MapGet("", ListPlayers.Handle);
            group.MapPost("", CreatePlayer.Handle);
        }
    }
}
