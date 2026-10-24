using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerForgotRequest(string? Email);

    public static class ForgotPassword
    {
        // POST /api/players/password/forgot: mails a reset code to a verified email. The answer
        // is always the same, whether or not the email is known and verified, so it can't be used
        // to find out which emails have accounts.
        public static async Task<IResult> Handle(Db db, IEmailSender mail, ILoggerFactory logs, PlayerForgotRequest request)
        {
            var email = PlayerInputRules.NormalizeEmail(request.Email);
            if (email is null)
                return Results.BadRequest(new { error = "a valid email is required" });

            var identity = await db.PlayerIdentities.FirstOrDefaultAsync(i =>
                i.Provider == PlayerProvider.Email && i.Subject == email && i.VerifiedAt != null);
            if (identity is not null)
            {
                var code = await EmailCodeService.CreateCode(db, identity.PlayerId, EmailCodePurpose.Reset);
                if (code is not null)
                {
                    // Sent in the background, so a hit doesn't take noticeably longer than a miss.
                    var log = logs.CreateLogger(LogCategories.PlayerAuth);
                    _ = Task.Run(async () =>
                    {
                        try { await EmailCodeService.SendCodeMail(mail, email, EmailCodePurpose.Reset, code); }
                        catch (Exception e) { log.LogError(e, "Could not send the password reset email"); }
                    });
                }
            }
            return Results.Ok(new { ok = true });
        }
    }
}
