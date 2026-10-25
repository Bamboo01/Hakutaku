using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Constants;
using server.Hosting;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerResetRequest(string? Email, string? Code, string? NewPassword);

    public static class ResetPassword
    {
        // post /api/players/password/reset: sets a new password if the code from password/forgot is right.
        // revokes every session the player has, since a reset usually means something went wrong, so they
        // sign in again by email. no device token to revoke: a reset needs a verified email, and
        // verifying already removed it
        public static async Task<IResult> Handle(Db db, EmailCodeService codes, TimeProvider time, PlayerResetRequest request)
        {
            var email = PlayerInputRules.NormalizeEmail(request.Email);
            var code = request.Code?.Trim();
            if (email is null || string.IsNullOrEmpty(request.NewPassword)
                || request.NewPassword.Length < Limits.MinPasswordLength || request.NewPassword.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = ErrorMessages.ResetPasswordInvalid });

            var identity = await db.PlayerIdentities.FirstOrDefaultAsync(i =>
                i.Provider == PlayerProvider.Email && i.Subject == email && i.VerifiedAt != null);
            if (identity is null || !EmailCodeService.LooksLikeCode(code) || !await codes.RedeemCode(identity.PlayerId, EmailCodePurpose.Reset, code!))
                return Results.BadRequest(new { error = ErrorMessages.InvalidOrExpiredCode });

            var hash = PasswordHasher.Hash(request.NewPassword);
            var now = time.UtcNow();
            await db.PlayerIdentities
                .Where(i => i.PlayerId == identity.PlayerId && i.Provider == PlayerProvider.Email)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.PwHash, hash));
            await db.PlayerSessions
                .Where(s => s.PlayerId == identity.PlayerId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
            return Results.Ok(new { ok = true });
        }
    }
}
