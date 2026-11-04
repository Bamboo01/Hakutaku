/*!************************************************************************
\file   LinkEmail.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the POST /api/players/link/email route, which adds
        an email and password to a logged-in guest and mails a verification
        code. A taken email gets the same answer as a free one, and the real
        owner is notified instead, so the route cannot be used to find out
        which emails have accounts. An unverified link only holds its
        address for UnverifiedHold, after which the next link replaces it,
        so nobody can block an address they can't verify.

        Mapped in PlayerAuthRoutes.cs with the player-link rate limit. Uses
        PlayerSessionService to find the caller, EmailCodeService for the
        code, and LinkAttemptNotifier to warn the owner of a taken email.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using Npgsql;
using server.Auth;
using server.Constants;
using server.Hosting;
using server.Models;
using server.SEmail;
using server.Services;

namespace server.Endpoints.PlayerAuth
{
    public record LinkEmailRequest(string? Email, string? Password);

    public static class LinkEmail
    {
        // how long an unverified link keeps its address. anyone can type any address, so without this a guest
        // could link someone else's email, never verify it (the code goes to the real owner), and lock the
        // real owner out of it for good. a day is plenty to type back a 15-minute code, with resends
        internal static readonly TimeSpan UnverifiedHold = TimeSpan.FromHours(24);

        // post /api/players/link/email: needs the player token. adds an email + password and mails a
        // verification code. the device token keeps working until the email is verified, so a typo'd
        // address can't strand a guest
        public static async Task<IResult> Handle(
            Db db, PlayerSessionService sessions, EmailCodeService codes, LinkAttemptNotifier notifier,
            IEmailSender mail, TimeProvider time, ILoggerFactory logs, HttpContext http, LinkEmailRequest request)
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

            // taken is judged by the key, so a gmail alias of a taken address counts as taken too
            var key = PlayerInputRules.EmailKey(email);
            var holder = await db.PlayerIdentities
                .Where(i => i.Provider == PlayerProvider.Email && i.Subject == key)
                .Select(i => new { i.VerifiedAt, i.LinkedAt })
                .FirstOrDefaultAsync();

            // a taken email gets the same answer as a real link, so this route can't be used to find out which
            // emails have accounts, and no code is made for the caller. only a verified holder gets a heads-up:
            // an unverified one never proved the address is theirs, so there's nobody to warn. the key, not
            // what was typed, so trying lots of aliases can't dodge the once-an-hour limit
            if (holder?.VerifiedAt is not null)
            {
                notifier.NotifyOwner(key);
                return Linked(email, verificationSent: true);
            }
            var staleBefore = time.UtcNow() - UnverifiedHold;
            if (holder is not null && holder.LinkedAt >= staleBefore)
                return Linked(email, verificationSent: true);

            // dropping a stale unverified link and adding this one go together, so the address is never left
            // free for a third player to grab in between
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                if (holder is not null)
                {
                    // the conditions are checked again here, so a holder who verifies right now keeps the address
                    var dropped = await db.PlayerIdentities
                        .Where(i => i.Provider == PlayerProvider.Email && i.Subject == key
                            && i.VerifiedAt == null && i.LinkedAt < staleBefore)
                        .ExecuteDeleteAsync();
                    if (dropped == 0)
                        return Linked(email, verificationSent: true);
                }

                db.PlayerIdentities.Add(new PlayerIdentity
                {
                    PlayerId = player.Id,
                    Provider = PlayerProvider.Email,
                    Subject = key,
                    Email = email,
                    PwHash = pwHash,
                });

                try
                {
                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e))
                {
                    // either this player linked a second email at the same moment, or someone else linked this
                    // one a moment ago (so it's unverified, and nobody is warned). the violated index's name
                    // tells us which
                    if (e.InnerException is PostgresException { ConstraintName: "ux_player_identities_one_email" })
                        return Results.Json(new { error = ErrorMessages.EmailAlreadyLinked }, statusCode: StatusCodes.Status409Conflict);
                    return Linked(email, verificationSent: true);
                }
            }

            // the link is already saved so a mail failure shouldn't undo it, they can ask for another
            // code with /email/resend
            var sent = false;
            try
            {
                var (code, _) = await codes.CreateCode(player.Id, EmailCodePurpose.Verify);
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

            return Linked(email, sent);
        }

        // the one answer shape, for a real link and a taken email alike
        static IResult Linked(string email, bool verificationSent) =>
            Results.Ok(new { provider = PlayerIdentityService.ProviderName(PlayerProvider.Email), email, verified = false, verificationSent });
    }
}
