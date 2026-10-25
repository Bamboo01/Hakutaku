using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.Characters
{
    // only the fields the client is allowed to send. the id is always made by the server, and the
    // player is just an id, so there's no way to slip in a new player or pick the character's id
    public record CreateCharacterRequest(Guid? PlayerId, string? Name);

    public static class CreateCharacter
    {
        // post /api/characters: saves a character for an existing player
        public static async Task<IResult> Handle(Db db, CreateCharacterRequest request)
        {
            var name = request.Name?.Trim();
            if (request.PlayerId is null || request.PlayerId == Guid.Empty
                || string.IsNullOrEmpty(name) || name.Length > Limits.CharacterNameMaxLength || name.Any(char.IsControl))
                return Results.BadRequest(new { error = ErrorMessages.CreateCharacterInvalid });

            var character = new Character { PlayerId = request.PlayerId.Value, Name = name };
            db.Characters.Add(character);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (DbErrors.IsForeignKeyViolation(e))
            {
                // no player has that id. catching it here (instead of checking first) also covers
                // a player being deleted between the check and the save
                return Results.BadRequest(new { error = ErrorMessages.NoSuchPlayer });
            }

            return Results.Ok(CharacterResponse.From(character));
        }
    }
}
