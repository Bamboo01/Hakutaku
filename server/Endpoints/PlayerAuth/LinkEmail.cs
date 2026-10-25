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
        // address can't strand a guest
        public static async Task<IResult> Handle(
            Db db, PlayerSessionService sessions, EmailCodeService codes, LinkAttemptNotifier notifier,
            IEmailSender mail, ILoggerFactory logs, HttpContext http, LinkEmailRequest request)
        {
            var player = await sessions.FindBySession(http);
            if (player is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            var email = PlayerInputRules.NormalizeEmail(request.Email);
            if (email is null || string.IsNullOrEmpty(request.Password)
                || request.Password.Length < Limits.MinPasswordLength || request.Password.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = ErrorMessages.LinkEmailInvalid });

            // this one is about the caller's own account, so it's fine to say it out loud
            if (await db.PlayerIdentities.AnyAsync(i => i.PlayerId == player.Id && i.Provider == PlayerProvider.Email))
                return Results.Json(new { error = ErrorMessages.EmailAlreadyLinked }, statusCode: StatusCodes.Status409Conflict);

            // hashed before we know whether the email is taken, so a taken email costs the same as a free one
            var pwHash = PasswordHasher.Hash(request.Password);

            var takenByAnother = await db.PlayerIdentities.AnyAsync(i => i.Provider == PlayerProvider.Email && i.Subject == email);
            if (!takenByAnother)
            {
                db.PlayerIdentities.Add(new PlayerIdentity
                {
                    PlayerId = player.Id,
                    Provider = PlayerProvider.Email,
                    Subject = email,
                    Email = email,
                    PwHash = pwHash,
                });

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e))
                {
                    // either this player linked a second email at the same moment, or someone else took this one
                    // a moment ago. the violated index's name tells us which
                    if (e.InnerException is PostgresException { ConstraintName: "ux_player_identities_one_email" })
                        return Results.Json(new { error = ErrorMessages.EmailAlreadyLinked }, statusCode: StatusCodes.Status409Conflict);
                    takenByAnother = true;
                }
            }

            if (takenByAnother)
            {
                // same answer as a real link, so this route can't be used to find out which emails have
                // accounts. no code is made for the caller, and the real owner gets a heads-up instead
                notifier.NotifyOwner(email);
                return Results.Ok(new { provider = PlayerIdentityService.ProviderName(PlayerProvider.Email), email, verified = false, verificationSent = true });
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
