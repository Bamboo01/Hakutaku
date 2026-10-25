/*!************************************************************************
\file   Register.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the POST /api/players/register route, which always
        makes a brand new guest. The server mints the device token (the
        guest's only credential) and returns it once, along with a session
        token. The hardware id is only recorded and never used to find or
        sign anyone in.

        Mapped in PlayerAuthRoutes.cs with the player-register rate limit.
        Uses PlayerSessionService, and only the hash of the device token is
        stored (see PlayerIdentityService).
**************************************************************************/
using System.Buffers.Text;
using System.Security.Cryptography;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerRegisterRequest(string? HardwareId, string? DisplayName);

    public static class Register
    {
        // post /api/players/register: makes a brand new guest every time. the server mints the device
        // token (the guest's only credential) and returns it once, the game keeps it and uses
        // login/device from then on
        public static async Task<IResult> Handle(Db db, PlayerSessionService sessions, PlayerRegisterRequest request)
        {
            // hardware id (e.g. unity's SystemInfo.deviceUniqueIdentifier) is only recorded, for support and
            // abuse tracking. it isn't secret so it never finds or signs in a player
            var hardwareId = request.HardwareId?.Trim();
            if (string.IsNullOrEmpty(hardwareId) || hardwareId.Length > Limits.MaxIdentifierLength)
                return Results.BadRequest(new { error = ErrorMessages.HardwareIdRequired });
            if (!PlayerInputRules.TryNormalizeDisplayName(request.DisplayName, out var displayName))
                return Results.BadRequest(new { error = ErrorMessages.DisplayNameRule });

            // 32 random bytes. only the hash of this is stored (see HashDeviceToken)
            var deviceToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var player = new Player { DisplayName = displayName, RegisteredHardwareId = hardwareId };
            db.Players.Add(player);
            db.PlayerIdentities.Add(new PlayerIdentity
            {
                Player = player,
                Provider = PlayerProvider.Device,
                Subject = PlayerIdentityService.HashDeviceToken(deviceToken),
            });

            // player, device identity and first session go in one save (one transaction), so a failure
            // can't leave a guest behind whose device token was never handed out
            var (token, expiresAt) = sessions.AddSession(player.Id);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = player.Id, displayName = player.DisplayName, xp = player.Xp, deviceToken, token, expiresAt });
        }
    }
}
