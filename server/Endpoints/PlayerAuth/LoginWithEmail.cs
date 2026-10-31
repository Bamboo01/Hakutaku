/*!************************************************************************
\file   LoginWithEmail.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the POST /api/players/login/email route, which
        signs in with a verified email and password. A wrong password and an
        unknown account get the same answer, and an unverified email gets a
        403.

        Mapped in PlayerAuthRoutes.cs with the player-login rate limit. Uses
        PasswordHasher and PlayerSessionService.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record LoginWithEmailRequest(string? Email, string? Password);

    public static class LoginWithEmail
    {
        // post /api/players/login/email: signs in with a verified email + password
        public static async Task<IResult> Handle(Db db, PlayerSessionService sessions, LoginWithEmailRequest request)
        {
            var email = PlayerInputRules.NormalizeEmail(request.Email);
            if (email is null || string.IsNullOrEmpty(request.Password) || request.Password.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = "email and password are required" });

            var identity = await db.PlayerIdentities
                .Include(i => i.Player)
                .FirstOrDefaultAsync(i => i.Provider == PlayerProvider.Email && i.Subject == PlayerInputRules.EmailKey(email));

            // unknown email still checks a dummy hash, so a miss takes as long as a wrong password and
            // never says which emails exist (same rule as admin login)
            var passwordOk = PasswordHasher.Verify(identity?.PwHash ?? PasswordHasher.DummyHash, request.Password);
            if (identity is null || !passwordOk)
                return Results.Json(new { error = ErrorMessages.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

            // an email only counts as a way in once verified, until then they're still a guest on their
            // device token. this check comes after the password one, so only someone who knows the
            // password finds out the email is unverified
            if (identity.VerifiedAt is null)
                return Results.Json(new { error = "this email isn't verified yet; enter the code mailed to it first" }, statusCode: StatusCodes.Status403Forbidden);

            var (token, expiresAt) = await sessions.IssueSession(identity.PlayerId);
            return Results.Ok(new { id = identity.PlayerId, displayName = identity.Player.DisplayName, xp = identity.Player.Xp, token, expiresAt });
        }
    }
}
