using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public static class ResendEmail
    {
        // POST /api/players/email/resend: needs the player token. Mails a new verification
        // code, at most once a minute per player.
        public static async Task<IResult> Handle(Db db, HttpContext http, IEmailSender mail, ILoggerFactory logs)
        {
            var player = await PlayerSessionService.FindBySession(db, http);
            if (player is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            var identity = await PlayerIdentityService.FindEmailIdentity(db, player.Id);
            if (identity is null)
                return Results.BadRequest(new { error = ErrorMessages.NoEmailLinked });
            if (identity.VerifiedAt is not null)
                return Results.Json(new { error = "email is already verified" }, statusCode: StatusCodes.Status409Conflict);

            // Null means a code was already made in the last minute.
            var code = await EmailCodeService.CreateCode(db, player.Id, EmailCodePurpose.Verify);
            if (code is null)
                return Results.Json(new { error = $"wait {EmailCodeService.ResendSeconds} seconds between codes" }, statusCode: StatusCodes.Status429TooManyRequests);

            try
            {
                await EmailCodeService.SendCodeMail(mail, identity.Email!, EmailCodePurpose.Verify, code);
            }
            catch (Exception e)
            {
                logs.CreateLogger(LogCategories.PlayerAuth).LogError(e, "Could not send the verification email");
                return Results.Json(new { error = "could not send the email, try again later" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            return Results.Ok(new { sent = true });
        }
    }
}
