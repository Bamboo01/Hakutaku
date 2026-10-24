using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerDeviceLoginRequest(string? DeviceToken);

    public static class LoginWithDevice
    {
        // POST /api/players/login/device: signs a guest in with the token register gave it.
        // It never creates anything. An unknown token is a 401, and the game should then offer
        // "new game" or "I have an account" instead of quietly starting a fresh player.
        // Tokens are 256 random bits, so guessing is hopeless; the rate limit is only about load.
        public static async Task<IResult> Handle(Db db, PlayerDeviceLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DeviceToken) || request.DeviceToken.Length > Limits.MaxIdentifierLength)
                return Results.BadRequest(new { error = "deviceToken is required" });

            var player = await PlayerIdentityService.FindByIdentity(
                db, PlayerProvider.Device, PlayerIdentityService.HashDeviceToken(request.DeviceToken));
            if (player is null)
                return Results.Json(new { error = "unknown device token" }, statusCode: StatusCodes.Status401Unauthorized);

            var (token, expiresAt) = await PlayerSessionService.IssueSession(db, player.Id);
            return Results.Ok(new { id = player.Id, displayName = player.DisplayName, xp = player.Xp, token, expiresAt });
        }
    }
}
