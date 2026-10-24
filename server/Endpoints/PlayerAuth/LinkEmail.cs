using Microsoft.EntityFrameworkCore;
using Npgsql;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record LinkEmailRequest(string? Email, string? Password);

    public static class LinkEmail
    {
        // POST /api/players/link/email: needs the player token. Adds an email and password to
        // the player and mails a verification code. The device token keeps working until the
        // email is verified (email/verify removes it), so a mistyped address can't strand a guest.
        // Not rate limited: it needs a session, and the cheap existence checks run before the
        // Argon2 hash, so the expensive work happens at most once per session.
        public static async Task<IResult> Handle(Db db, HttpContext http, IEmailSender mail, ILoggerFactory logs, LinkEmailRequest request)
        {
            var player = await PlayerSessionService.FindBySession(db, http);
            if (player is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            var email = PlayerInputRules.NormalizeEmail(request.Email);
            if (email is null || string.IsNullOrEmpty(request.Password)
                || request.Password.Length < Limits.MinPasswordLength || request.Password.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = ErrorMessages.LinkEmailInvalid });

            if (await db.PlayerIdentities.AnyAsync(i => i.PlayerId == player.Id && i.Provider == PlayerProvider.Email))
                return Results.Json(new { error = ErrorMessages.EmailAlreadyLinked }, statusCode: StatusCodes.Status409Conflict);
            if (await db.PlayerIdentities.AnyAsync(i => i.Provider == PlayerProvider.Email && i.Subject == email))
                return Results.Json(new { error = ErrorMessages.EmailInUse }, statusCode: StatusCodes.Status409Conflict);

            db.PlayerIdentities.Add(new PlayerIdentity
            {
                PlayerId = player.Id,
                Provider = PlayerProvider.Email,
                Subject = email,
                Email = email,
                PwHash = AdminAuth.HashPassword(request.Password),
            });

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (PlayerIdentityService.IsUniqueViolation(e))
            {
                // Either this player linked a second email at the same moment, or someone else
                // took this one. The name of the violated index says which.
                var oneEmailPerPlayer = e.InnerException is PostgresException { ConstraintName: "ux_player_identities_one_email" };
                return Results.Json(
                    new { error = oneEmailPerPlayer ? ErrorMessages.EmailAlreadyLinked : ErrorMessages.EmailInUse },
                    statusCode: StatusCodes.Status409Conflict);
            }

            // The link is already saved, so a mail failure must not undo it. The player can
            // ask for another code with /email/resend.
            var sent = false;
            try
            {
                var code = await EmailCodeService.CreateCode(db, player.Id, EmailCodePurpose.Verify);
                if (code is not null)
                {
                    await EmailCodeService.SendCodeMail(mail, email, EmailCodePurpose.Verify, code);
                    sent = true;
                }
            }
            catch (Exception e)
            {
                logs.CreateLogger(LogCategories.PlayerAuth).LogError(e, "Could not send the verification email");
            }

            return Results.Ok(new { provider = PlayerIdentityService.ProviderName(PlayerProvider.Email), email, verified = false, verificationSent = sent });
        }
    }
}
