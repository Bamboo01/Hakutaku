/*!************************************************************************
\file   ListPlayers.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the GET /api/players route, which lists every
        player with just the names of the providers they have linked. The
        identities themselves stay out, because emails are personal data.

        Mapped in PlayerRoutes.cs (admin only).
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Models;
using server.Services;

namespace server.Endpoints.Players
{
    public static class ListPlayers
    {
        // get /api/players: every player, with just the names of the providers they've linked. the identities
        // stay out: emails are personal data, and the hardware id shouldn't show in a casual listing
        public static async Task<IResult> Handle(Db db)
        {
            var players = await db.Players
                .OrderBy(p => p.Id)
                .Select(p => new
                {
                    id = p.Id,
                    displayName = p.DisplayName,
                    xp = p.Xp,
                    providers = db.PlayerIdentities.Where(i => i.PlayerId == p.Id).Select(i => i.Provider).ToList(),
                })
                .ToListAsync();

            // provider names are made in memory, the db can't run our method
            return Results.Ok(players.Select(p => new
            {
                p.id,
                p.displayName,
                p.xp,
                providers = p.providers.Select(PlayerIdentityService.ProviderName).Order().ToList(),
            }));
        }
    }
}
