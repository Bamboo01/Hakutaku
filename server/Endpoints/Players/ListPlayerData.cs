/*!************************************************************************
\file   ListPlayerData.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines the GET /api/players/{id}/data route, which gives
        an admin everything a player has in player data: all three tiers,
        each with its keys, values and when each was last set. The admin
        UI's player page shows it as three key and value tables.

        Mapped in PlayerRoutes.cs, so it needs an admin session. A game
        server reads one tier at a time with its own route in
        Endpoints/GameServer. Uses PlayerDataService.
**************************************************************************/
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.Players
{
    public static class ListPlayerData
    {
        // get /api/players/{id}/data: answers { tiers: [{ tier, entries: [{ key, value, updatedAt }] }] }.
        // every tier is always there, in the enum's order, even with no entries, so the page can draw
        // all three tables from the answer alone
        public static async Task<IResult> Handle(PlayerDataService data, Guid id)
        {
            if (!await data.PlayerExists(id))
                return Results.NotFound(new { error = ErrorMessages.NoSuchPlayer });

            var entries = await data.ReadAll(id);
            return Results.Ok(new
            {
                tiers = Enum.GetValues<PlayerDataTier>().Select(tier => new
                {
                    tier = tier.ToString(),
                    entries = entries
                        .Where(e => e.Tier == tier)
                        .Select(e => new { key = e.Key, value = e.Value, updatedAt = e.UpdatedAt }),
                }),
            });
        }
    }
}
