/*!************************************************************************
\file   ForgotPassword.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the POST /api/players/password/forgot route, which
        mails a reset code to a verified email. The answer is always the
        same whether or not the email exists, so the route cannot be used to
        find out who has an account.

        Mapped in PlayerAuthRoutes.cs with the player-mail rate limit. Uses
        EmailCodeService and IEmailSender. The code is redeemed by
        ResetPassword.cs.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.SEmail;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record PlayerForgotRequest(string? Email);

    public static class ForgotPassword
    {
        // post /api/players/password/forgot: mails a reset code to a verified email. the answer is always
        // the same whether or not the email exists, so it can't be used to find out who has an account
        public static async Task<IResult> Handle(Db db, EmailCodeService codes, IEmailSender mail, ILoggerFactory logs, PlayerForgotRequest request)
        {
            var email = PlayerInputRules.NormalizeEmail(request.Email);
            if (email is null)
                return Results.BadRequest(new { error = "a valid email is required" });

            var identity = await db.PlayerIdentities.FirstOrDefaultAsync(i =>
                i.Provider == PlayerProvider.Email && i.Subject == PlayerInputRules.EmailKey(email) && i.VerifiedAt != null);
            if (identity is not null)
            {
                // a refusal (too soon, or today's codes used up) stays silent, so the answer is the same
                // as for an email with no account
                var (code, _) = await codes.CreateCode(identity.PlayerId, EmailCodePurpose.Reset);
                if (code is not null)
                {
                    // sent in the background so a hit isn't noticeably slower than a miss
                    var log = logs.CreateLogger(LogCategories.PlayerAuth);
                    _ = Task.Run(async () =>
                    {
                        try { await EmailCodeService.SendCodeMail(mail, identity.Email!, EmailCodePurpose.Reset, code); }
                        catch (Exception e) { log.LogError(e, "Could not send the password reset email"); }
                    });
                }
            }
            return Results.Ok(new { ok = true });
        }
    }
}
