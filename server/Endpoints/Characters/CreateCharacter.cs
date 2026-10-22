using server.Models;

namespace server.Endpoints.Characters
{
    public static class CreateCharacter
    {
        // POST /api/characters: saves a character for an existing player.
        // The client still sends a whole Character entity; a request record replaces that later.
        public static async Task<IResult> Handle(Db db, Character c)
        {
            db.Characters.Add(c);
            await db.SaveChangesAsync();
            return Results.Ok(c);
        }
    }
}
