/*!************************************************************************
\file   CharacterRoutes.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines MapCharacterRoutes, which maps GET and POST
        /api/characters and puts the admin check on the whole group. It is
        the only file that knows these URLs and who can call them.

        Called from Program.cs. Handlers are ListCharacters.cs and
        CreateCharacter.cs. Uses RequireAdmin from AdminGuards.cs.
**************************************************************************/
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
