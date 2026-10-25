/*!************************************************************************
\file   CreateCharacter.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the POST /api/characters route, which saves a
        character for an existing player. The request record carries only a
        player id and a name, so a client cannot pick the character's id or
        sneak in a new player. An unknown player id gets a 400.

        Mapped in CharacterRoutes.cs. Uses Limits for the name length,
        DbErrors to spot a missing player and CharacterResponse for the
        reply.
**************************************************************************/
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
