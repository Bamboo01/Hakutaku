/*!************************************************************************
\file   ListCharacters.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the GET /api/characters route, which returns every
        character as CharacterResponse records. There is no paging yet.

        Mapped in CharacterRoutes.cs.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Endpoints.Characters
{
    public static class ListCharacters
    {
        // get /api/characters: every character, no paging yet
        public static async Task<IResult> Handle(Db db) =>
            Results.Ok(await db.Characters
                .Select(c => new CharacterResponse(c.Id, c.PlayerId, c.Name))
                .ToListAsync());
    }
}
