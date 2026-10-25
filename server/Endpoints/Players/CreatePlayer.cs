/*!************************************************************************
\file   CreatePlayer.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the POST /api/players route, where an admin
        creates a player. That player cannot sign in, because credentials
        only come from the player auth routes.

        Mapped in PlayerRoutes.cs (admin only). Uses PlayerInputRules to
        check the display name.
**************************************************************************/
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.Players
{
    public record AdminCreatePlayerRequest(int Xp, string? DisplayName);

    public static class CreatePlayer
    {
        // post /api/players: an admin makes a player. it can't sign in, since credentials only come from the
        // player routes (register mints a device token, link/email adds an email)
        public static async Task<IResult> Handle(Db db, AdminCreatePlayerRequest request)
        {
            if (!PlayerInputRules.TryNormalizeDisplayName(request.DisplayName, out var displayName))
                return Results.BadRequest(new { error = ErrorMessages.DisplayNameRule });

            var player = new Player { Xp = request.Xp, DisplayName = displayName };
            db.Players.Add(player);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = player.Id, displayName = player.DisplayName, xp = player.Xp });
        }
    }
}
