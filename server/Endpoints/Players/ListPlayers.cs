using Microsoft.EntityFrameworkCore;
using server.Models;
using server.Services;

namespace server.Endpoints.Players
{
    public static class ListPlayers
    {
        // GET /api/players: every player, with only the names of the providers they have
        // linked. The identities themselves stay out: emails are personal data, and the
        // hardware ID shouldn't show up in a casual listing either.
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

            // Provider names are made in memory, since the database can't run our method.
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
