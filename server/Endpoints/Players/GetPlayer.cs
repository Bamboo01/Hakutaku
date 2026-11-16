/*!************************************************************************
\file   GetPlayer.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines the GET /api/players/{id} route, which gives an
        admin one player for the admin UI's player overview: a row of the
        list (id, display name, providers, created, last login) plus each
        linked account with when it was linked and, for an email, whether
        it is verified.

        Mapped in PlayerRoutes.cs, so it needs an admin session. The email
        address itself and the hardware id are left out.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.Players
{
    public static class GetPlayer
    {
        // get /api/players/{id}: the list's fields plus `accounts: [{ provider, linkedAt, verified }]`. like the
        // list, it leaves the subjects and emails out: emails are personal data, and nothing on the page needs them.
        // `verified` is only meaningful for an email; a device is always true, since it has nothing to confirm
        public static async Task<IResult> Handle(Db db, Guid id)
        {
            var player = await db.Players
                .Where(p => p.Id == id)
                .Select(p => new
                {
                    id = p.Id,
                    displayName = p.DisplayName,
                    accounts = db.PlayerIdentities
                        .Where(i => i.PlayerId == p.Id)
                        .Select(i => new { i.Provider, i.LinkedAt, i.VerifiedAt })
                        .ToList(),
                    lastLoginAt = db.PlayerSessions.Where(s => s.PlayerId == p.Id).Max(s => (DateTime?)s.IssuedAt),
                })
                .FirstOrDefaultAsync();
            if (player is null)
                return Results.NotFound(new { error = ErrorMessages.NoSuchPlayer });

            // provider names and the creation time are made in memory, the db can't run our methods
            var accounts = player.accounts
                .Select(a => new
                {
                    provider = PlayerIdentityService.ProviderName(a.Provider),
                    linkedAt = a.LinkedAt,
                    verified = a.Provider != PlayerProvider.Email || a.VerifiedAt is not null,
                })
                .OrderBy(a => a.provider)
                .ToList();

            return Results.Ok(new
            {
                player.id,
                player.displayName,
                providers = accounts.Select(a => a.provider).ToList(),
                createdAt = PlayerIds.CreatedAt(player.id),
                player.lastLoginAt,
                accounts,
            });
        }
    }
}
