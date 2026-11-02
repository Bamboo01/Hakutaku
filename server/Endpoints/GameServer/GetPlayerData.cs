/*!************************************************************************
\file   GetPlayerData.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the GET /api/server/players/{id}/data route. A
        game server reads a player's data in one tier, for example the Xp
        key when a player joins. It can name the keys it wants, and a key
        that doesn't exist is just left out. With no keys it gets every key
        in the tier. The tier is required.

        Mapped in GameServerRoutes.cs, so it needs a server key. Uses
        PlayerDataService and PlayerDataRules.
**************************************************************************/
using server.Constants;
using server.Services;

namespace server.Endpoints.GameServer
{
    public static class GetPlayerData
    {
        // get /api/server/players/{id}/data?tier=ReadOnly&keys=Xp,Foo: answers { data: { "Xp": "1200" } }.
        // the input is checked first (400), then the player is looked up (404)
        public static async Task<IResult> Handle(PlayerDataService data, string id, string? tier, string? keys)
        {
            if (!PlayerDataRules.TryParseTier(tier, out var parsedTier))
                return Results.BadRequest(new { error = ErrorMessages.PlayerDataTierInvalid });

            List<string>? wanted = null;
            if (keys is not null && !PlayerDataRules.TryParseKeys(keys, out wanted))
                return Results.BadRequest(new { error = ErrorMessages.PlayerDataKeyInvalid });

            if (!Guid.TryParse(id, out var playerId) || !await data.PlayerExists(playerId))
                return Results.NotFound(new { error = ErrorMessages.NoSuchPlayer });

            return Results.Ok(new { data = await data.Read(playerId, parsedTier, wanted) });
        }
    }
}
