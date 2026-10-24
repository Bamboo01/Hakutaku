using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.Players
{
    public record AdminCreatePlayerRequest(int Xp, string? DisplayName);

    public static class CreatePlayer
    {
        // POST /api/players: an admin makes a player. It has no way to sign in, because
        // credentials only come from the player routes (register mints a device token,
        // link/email adds an email).
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
