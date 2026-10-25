using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Endpoints.Characters
{
    public static class ListCharacters
    {
        // get /api/characters: every character, no paging yet
        public static async Task<IResult> Handle(Db db) =>
            Results.Ok(await db.Characters.ToListAsync());
    }
}
