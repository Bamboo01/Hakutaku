using Microsoft.EntityFrameworkCore;
using Npgsql;
using server.Auth;
using server.Constants;
using server.Models;
using server.SEmail;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record LinkEmailRequest(string? Email, string? Password);

    public static class LinkEmail
    {
        // post /api/players/link/email: needs the player token. adds an email + password and mails a
        // verification code. the device token keeps working until the email is verified, so a typo'd
        // address can't strand a guest. no rate limit: it needs a session, and the cheap checks run
        // before the argon2 hash, so the expensive part happens at most once per session
        public static async Task<IResult> Handle(
            Db db, PlayerSessionService sessions, EmailCodeService codes, IEmailSender mail, ILoggerFactory logs,
            HttpContext http, LinkEmailRequest request)
        {
            var player = await sessions.FindBySession(http);
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
                PwHash = PasswordHasher.Hash(request.Password),
            });

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (PlayerIdentityService.IsUniqueViolation(e))
            {
                // either this player linked a second email at the same moment, or someone else took this one.
                // the violated index's name tells us which
                var oneEmailPerPlayer = e.InnerException is PostgresException { ConstraintName: "ux_player_identities_one_email" };
                return Results.Json(
                    new { error = oneEmailPerPlayer ? ErrorMessages.EmailAlreadyLinked : ErrorMessages.EmailInUse },
                    statusCode: StatusCodes.Status409Conflict);
            }

            // the link is already saved so a mail failure shouldn't undo it, they can ask for another
            // code with /email/resend
            var sent = false;
            try
            {
                var code = await codes.CreateCode(player.Id, EmailCodePurpose.Verify);
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
