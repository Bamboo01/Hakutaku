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
        // POST /api/players/register: makes a new guest player, every time. The server mints
        // the device token (the guest's only credential) and returns it once. The game keeps
        // it and signs in with login/device from then on.
        public static async Task<IResult> Handle(Db db, PlayerRegisterRequest request)
        {
            // The hardware ID (e.g. Unity's SystemInfo.deviceUniqueIdentifier) is only recorded,
            // for support and abuse tracking. It isn't secret, so it never finds or signs in a player.
            var hardwareId = request.HardwareId?.Trim();
            if (string.IsNullOrEmpty(hardwareId) || hardwareId.Length > Limits.MaxIdentifierLength)
                return Results.BadRequest(new { error = ErrorMessages.HardwareIdRequired });
            if (!PlayerInputRules.TryNormalizeDisplayName(request.DisplayName, out var displayName))
                return Results.BadRequest(new { error = ErrorMessages.DisplayNameRule });

            // 32 random bytes. Only the hash of this is stored (see HashDeviceToken).
            var deviceToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var player = new Player { DisplayName = displayName, RegisteredHardwareId = hardwareId };
            db.Players.Add(player);
            db.PlayerIdentities.Add(new PlayerIdentity
            {
                Player = player,
                Provider = PlayerProvider.Device,
                Subject = PlayerIdentityService.HashDeviceToken(deviceToken),
            });
            await db.SaveChangesAsync();

            var (token, expiresAt) = await PlayerSessionService.IssueSession(db, player.Id);
            return Results.Ok(new { id = player.Id, displayName = player.DisplayName, xp = player.Xp, deviceToken, token, expiresAt });
        }
    }
}
