using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerDisplayNameRequest(string? DisplayName);

    public static class SetDisplayName
    {
        // POST /api/players/display-name: needs the player token. Sets or replaces the name.
        // Not rate limited, since it needs a session and is one cheap UPDATE.
        public static async Task<IResult> Handle(Db db, HttpContext http, PlayerDisplayNameRequest request)
        {
            var player = await PlayerSessionService.FindBySession(db, http);
            if (player is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            if (request.DisplayName is null || !PlayerInputRules.TryNormalizeDisplayName(request.DisplayName, out var displayName))
                return Results.BadRequest(new { error = ErrorMessages.DisplayNameRule });

            await db.Players
                .Where(p => p.Id == player.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, displayName));
            return Results.Ok(new { displayName });
        }
    }
}
