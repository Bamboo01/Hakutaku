using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record LoginWithEmailRequest(string? Email, string? Password);

    public static class LoginWithEmail
    {
        // POST /api/players/login/email: signs in with a verified email and password.
        public static async Task<IResult> Handle(Db db, LoginWithEmailRequest request)
        {
            var email = PlayerInputRules.NormalizeEmail(request.Email);
            if (email is null || string.IsNullOrEmpty(request.Password) || request.Password.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = "email and password are required" });

            var identity = await db.PlayerIdentities
                .Include(i => i.Player)
                .FirstOrDefaultAsync(i => i.Provider == PlayerProvider.Email && i.Subject == email);

            // An unknown email still checks a dummy hash, so a miss takes as long as a wrong
            // password and the answer never says which emails exist (same rule as admin login).
            var passwordOk = AdminAuth.VerifyPassword(identity?.PwHash ?? AdminAuth.DummyHash, request.Password);
            if (identity is null || !passwordOk)
                return Results.Json(new { error = ErrorMessages.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

            // An email is only a way in once verified. Until then the player is still a guest on
            // their device token. This comes after the password check, so only someone who
            // knows the password learns that the email is unverified.
            if (identity.VerifiedAt is null)
                return Results.Json(new { error = "this email isn't verified yet; enter the code mailed to it first" }, statusCode: StatusCodes.Status403Forbidden);

            var (token, expiresAt) = await PlayerSessionService.IssueSession(db, identity.PlayerId);
            return Results.Ok(new { id = identity.PlayerId, displayName = identity.Player.DisplayName, xp = identity.Player.Xp, token, expiresAt });
        }
    }
}
