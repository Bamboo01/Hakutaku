/*!************************************************************************
\file   LoginWithDevice.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the POST /api/players/login/device route, which
        signs a guest in with the device token that register gave them. It
        never creates anything: an unknown token is a 401, so the game can
        offer "new game" or "I have an account" instead of quietly starting
        a fresh player.

        Mapped in PlayerAuthRoutes.cs with the player-device-login rate
        limit. Uses PlayerIdentityService to look up the token and
        PlayerSessionService to issue the session.
**************************************************************************/
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerDeviceLoginRequest(string? DeviceToken);

    public static class LoginWithDevice
    {
        // post /api/players/login/device: signs a guest in with the token register gave them. never creates
        // anything, an unknown token is a 401 and the game should offer "new game" / "i have an account"
        // instead of quietly starting a fresh player. tokens are 256 random bits so guessing is hopeless,
        // the rate limit is just about load
        public static async Task<IResult> Handle(PlayerIdentityService identities, PlayerSessionService sessions, PlayerDeviceLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DeviceToken) || request.DeviceToken.Length > Limits.MaxIdentifierLength)
                return Results.BadRequest(new { error = "deviceToken is required" });

            var player = await identities.FindByIdentity(
                PlayerProvider.Device, PlayerIdentityService.HashDeviceToken(request.DeviceToken));
            if (player is null)
                return Results.Json(new { error = "unknown device token" }, statusCode: StatusCodes.Status401Unauthorized);

            var (token, expiresAt) = await sessions.IssueSession(player.Id);
            return Results.Ok(new { id = player.Id, displayName = player.DisplayName, xp = player.Xp, token, expiresAt });
        }
    }
}
