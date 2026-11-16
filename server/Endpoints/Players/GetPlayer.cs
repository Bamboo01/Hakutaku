/*!************************************************************************
\file   GetPlayer.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines the GET /api/players/{id} route, which gives an
        admin one player: their id, display name and the names of the
        providers they have linked. It is what the admin UI's player page
        shows at the top.

        Mapped in PlayerRoutes.cs, so it needs an admin session. The shape
        is the same as one row of ListPlayers.cs.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.Players
{
    public static class GetPlayer
    {
        // get /api/players/{id}: one player, in the same shape as a row of the list. like the list, it
        // leaves the identities out: emails are personal data, and nothing on the page needs them
        public static async Task<IResult> Handle(Db db, Guid id)
        {
            var player = await db.Players
                .Where(p => p.Id == id)
                .Select(p => new
                {
                    id = p.Id,
                    displayName = p.DisplayName,
                    providers = db.PlayerIdentities.Where(i => i.PlayerId == p.Id).Select(i => i.Provider).ToList(),
                })
                .FirstOrDefaultAsync();
            if (player is null)
                return Results.NotFound(new { error = ErrorMessages.NoSuchPlayer });

            // provider names are made in memory, the db can't run our method
            return Results.Ok(new
            {
                player.id,
                player.displayName,
                providers = player.providers.Select(PlayerIdentityService.ProviderName).Order().ToList(),
            });
        }
    }
}
