using server.Auth;

namespace server.Endpoints.Characters
{
    public static class CharacterRoutes
    {
        // only file that knows the urls and who can call them, one line per route
        public static void MapCharacterRoutes(this IEndpointRouteBuilder app)
        {
            // admin only for now, so the check goes on the whole group and not each route
            var group = app.MapGroup("/api/characters").RequireAdmin();

            group.MapGet("", ListCharacters.Handle);
            group.MapPost("", CreateCharacter.Handle);
        }
    }
}
