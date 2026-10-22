namespace server.Endpoints.Characters
{
    public static class CharacterRoutes
    {
        // The only file that knows the URLs and who may call them: one line per route.
        public static void MapCharacterRoutes(this IEndpointRouteBuilder app)
        {
            // Admin-only for now, so the check goes on the whole group instead of each route.
            var group = app.MapGroup("/api/characters").RequireAdmin();

            group.MapGet("", ListCharacters.Handle);
            group.MapPost("", CreateCharacter.Handle);
        }
    }
}
