using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Hosting;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerCodeRequest(string? Code);

    public static class VerifyEmail
    {
        // post /api/players/email/verify: needs the player token. redeems the code from link/email or
        // email/resend. a verified email makes the account recoverable, so the device token (a credential
        // that never expires, sitting on one phone) is deleted in the same transaction. the session in use
        // carries on, and deviceTokenRemoved tells the game to drop its stored copy
        public static async Task<IResult> Handle(
            Db db, PlayerSessionService sessions, PlayerIdentityService identities, EmailCodeService codes,
            TimeProvider time, HttpContext http, PlayerCodeRequest request)
        {
            var player = await sessions.FindBySession(http);
            if (player is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            var identity = await identities.FindEmailIdentity(player.Id);
            if (identity is null)
                return Results.BadRequest(new { error = ErrorMessages.NoEmailLinked });
            if (identity.VerifiedAt is not null)
                return Results.Ok(new { verified = true, deviceTokenRemoved = false });

            var code = request.Code?.Trim();
            if (!EmailCodeService.LooksLikeCode(code) || !await codes.RedeemCode(player.Id, EmailCodePurpose.Verify, code!))
                return Results.BadRequest(new { error = ErrorMessages.InvalidOrExpiredCode });

            var now = time.UtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.PlayerIdentities
                .Where(i => i.PlayerId == player.Id && i.Provider == PlayerProvider.Email)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.VerifiedAt, now));
            var removed = await db.PlayerIdentities
                .Where(i => i.PlayerId == player.Id && i.Provider == PlayerProvider.Device)
                .ExecuteDeleteAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { verified = true, deviceTokenRemoved = removed > 0 });
        }
    }
}
