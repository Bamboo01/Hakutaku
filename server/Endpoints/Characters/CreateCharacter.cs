using server.Models;

namespace server.Endpoints.Characters
{
    public static class CreateCharacter
    {
        // post /api/characters: saves a character for an existing player. still takes a whole Character
        // entity from the client, a request record replaces that later
        public static async Task<IResult> Handle(Db db, Character c)
        {
            db.Characters.Add(c);
            await db.SaveChangesAsync();
            return Results.Ok(c);
        }
    }
}
