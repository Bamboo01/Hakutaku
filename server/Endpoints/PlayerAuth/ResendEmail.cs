using server.Constants;
using server.Models;
using server.SEmail;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public static class ResendEmail
    {
        // post /api/players/email/resend: needs the player token. mails a new verification code,
        // max once a minute per player
        public static async Task<IResult> Handle(
            PlayerSessionService sessions, PlayerIdentityService identities, EmailCodeService codes,
            IEmailSender mail, ILoggerFactory logs, HttpContext http)
        {
            var player = await sessions.FindBySession(http);
            if (player is null)
                return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);

            var identity = await identities.FindEmailIdentity(player.Id);
            if (identity is null)
                return Results.BadRequest(new { error = ErrorMessages.NoEmailLinked });
            if (identity.VerifiedAt is not null)
                return Results.Json(new { error = "email is already verified" }, statusCode: StatusCodes.Status409Conflict);

            // null means a code was already made in the last minute
            var code = await codes.CreateCode(player.Id, EmailCodePurpose.Verify);
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
