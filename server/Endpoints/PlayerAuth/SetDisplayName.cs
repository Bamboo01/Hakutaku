using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerDisplayNameRequest(string? DisplayName);

    public static class SetDisplayName
    {
        // post /api/players/display-name: needs the player token. sets or replaces the name.
        // no rate limit, it needs a session and it's one cheap update
        public static async Task<IResult> Handle(Db db, PlayerSessionService sessions, HttpContext http, PlayerDisplayNameRequest request)
        {
            var player = await sessions.FindBySession(http);
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
