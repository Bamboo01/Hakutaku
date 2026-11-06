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

            var set = new Dictionary<string, string>();
            foreach (var (key, value) in request.Data ?? [])
            {
                if (!PlayerDataRules.IsValidKey(key))
                    return Results.BadRequest(new { error = ErrorMessages.PlayerDataKeyInvalid });
                // a null value is refused, removing a key is what keysToRemove is for
                if (!PlayerDataRules.IsValidValue(value))
                    return Results.BadRequest(new { error = ErrorMessages.PlayerDataValueInvalid });
                set[key] = value!;
            }

            var remove = new List<string>();
            foreach (var key in request.KeysToRemove ?? [])
            {
                if (!PlayerDataRules.IsValidKey(key))
                    return Results.BadRequest(new { error = ErrorMessages.PlayerDataKeyInvalid });
                if (set.ContainsKey(key))
                    return Results.BadRequest(new { error = ErrorMessages.PlayerDataKeyTwice });
                if (!remove.Contains(key)) remove.Add(key);
            }

            if (set.Count + remove.Count > Limits.PlayerDataMaxChangesPerRequest)
                return Results.BadRequest(new { error = ErrorMessages.PlayerDataTooManyChanges });

            if (!Guid.TryParse(id, out var playerId) || !await data.PlayerExists(playerId))
                return Results.NotFound(new { error = ErrorMessages.NoSuchPlayer });

            var updated = await data.Write(playerId, parsedTier, set, remove);
            if (updated is null)
                return Results.BadRequest(new { error = ErrorMessages.PlayerDataTooManyKeys });

            return Results.Ok(new { updated });
        }
    }
}
