using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Endpoints.Characters
{
    public static class ListCharacters
    {
        // GET /api/characters: every character. No paging yet.
        public static async Task<IResult> Handle(Db db) =>
            Results.Ok(await db.Characters.ToListAsync());
    }
}
