/*!************************************************************************
\file   ListPlayers.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the GET /api/players route, which lists every
        player with the names of the providers they have linked, when they
        were made and when they last signed in. The identities themselves
        stay out, because emails are personal data.

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
                    providers = db.PlayerIdentities.Where(i => i.PlayerId == p.Id).Select(i => i.Provider).ToList(),
                    // a sign-in makes a session, so the newest one is the last login. null if they never signed in
                    lastLoginAt = db.PlayerSessions.Where(s => s.PlayerId == p.Id).Max(s => (DateTime?)s.IssuedAt),
                })
                .ToListAsync();

            // provider names and the creation time are made in memory, the db can't run our methods
            return Results.Ok(players.Select(p => new
            {
                p.id,
                p.displayName,
                providers = p.providers.Select(PlayerIdentityService.ProviderName).Order().ToList(),
                createdAt = PlayerIds.CreatedAt(p.id),
                p.lastLoginAt,
            }));
        }
    }
}
