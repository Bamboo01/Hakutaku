/*!************************************************************************
\file   UpdatePlayerData.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the POST /api/server/players/{id}/data route. A
        game server sets keys and removes keys in one tier of a player's
        data, all in one transaction. A write sets values and never adds to
        them, so repeating it is harmless. The tier is required, and
        anything over a limit gets a 400.

        Mapped in GameServerRoutes.cs, so it needs a server key. Uses
        PlayerDataService and PlayerDataRules, and the numbers in Limits.cs.
**************************************************************************/
using server.Constants;
using server.Services;

namespace server.Endpoints.GameServer
{
    public record UpdatePlayerDataRequest(Dictionary<string, string?>? Data, List<string>? KeysToRemove);

    public static class UpdatePlayerData
    {
        // post /api/server/players/{id}/data?tier=ReadOnly with { data: { k: v }, keysToRemove: [k] }:
        // answers { updated: n }. every limit is checked before anything is written
        public static async Task<IResult> Handle(PlayerDataService data, string id, string? tier, UpdatePlayerDataRequest request)
        {
            if (!PlayerDataRules.TryParseTier(tier, out var parsedTier))
                return Results.BadRequest(new { error = ErrorMessages.PlayerDataTierInvalid });

            if (!PlayerDataRules.TryReadChanges(request.Data, request.KeysToRemove, out var set, out var remove, out var error))
                return Results.BadRequest(new { error });

            if (!Guid.TryParse(id, out var playerId) || !await data.PlayerExists(playerId))
                return Results.NotFound(new { error = ErrorMessages.NoSuchPlayer });

            var updated = await data.Write(playerId, parsedTier, set, remove);
            if (updated is null)
                return Results.BadRequest(new { error = ErrorMessages.PlayerDataTooManyKeys });

            return Results.Ok(new { updated });
        }
    }
}
