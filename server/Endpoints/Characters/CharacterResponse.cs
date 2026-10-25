/*!************************************************************************
\file   CharacterResponse.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines CharacterResponse, the small record the API sends
        back for a character, so the whole database entity (with its nested
        player) never leaves the server.

        Used by CreateCharacter.cs and ListCharacters.cs. Built from the
        Character model.
**************************************************************************/
using server.Models;

namespace server.Endpoints.Characters
{
    // what the api sends back for a character: just the fields a client needs, not the whole
    // db entity (which would also carry the nested player)
    public record CharacterResponse(Guid Id, Guid PlayerId, string Name)
    {
        public static CharacterResponse From(Character c) => new(c.Id, c.PlayerId, c.Name);
    }
}
