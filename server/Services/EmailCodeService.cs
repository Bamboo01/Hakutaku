/*!************************************************************************
\file   EmailCodeService.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines EmailCodeService, which makes and redeems the
        6-digit codes mailed for verifying an email and resetting a
        password. Codes expire, allow only a few wrong tries, can only be
        re-sent once a minute, and are capped per account each day.

        Used by LinkEmail.cs, ResendEmail.cs, VerifyEmail.cs,
        ForgotPassword.cs and ResetPassword.cs. Stores PlayerEmailCode rows
        and mails through IEmailSender.
**************************************************************************/
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Constants;
using server.Hosting;
using server.Models;
using server.SEmail;

namespace server.Services
{
    // the 6-digit codes we mail for verifying an email and resetting a password. making and
    // redeeming a code uses the db and the clock so those need an instance, the rest is pure
    public class EmailCodeService(Db db, TimeProvider time)
    {
        const int CodeMinutes = 15;

        // six digits is only a million guesses, so a code dies after this many wrong tries
        const int MaxCodeAttempts = 5;

        // minimum gap between mails to one player for the same purpose, so the routes
        // can't be used to flood someone's inbox
        internal const int ResendSeconds = 60;

        // only a hash of the code is stored, tied to the player and what it's for
        static byte[] HashCode(Guid playerId, EmailCodePurpose purpose, string code) =>
            TokenHasher.Hash($"{playerId}:{(short)purpose}:{code}");

        internal static bool LooksLikeCode(string? code) =>
            code is { Length: 6 } && code.All(char.IsAsciiDigit);

        // makes a fresh code and retires any older unused one. makes nothing, and says why, if one was
        // already made in the last ResendSeconds or the account already had its CodesPerDay today
        public async Task<(string? Code, CodeRefusal Refusal)> CreateCode(Guid playerId, EmailCodePurpose purpose)
        {
            // check, retire, insert all happen in one transaction, so a failure part way can't leave
            // the old code retired with no new one. the lock makes parallel requests for the same
            // player take turns, otherwise two could both pass the cooldown check and both send a mail
            await using var transaction = await db.Database.BeginTransactionAsync();
            var lockKey = $"email-code:{playerId}:{(short)purpose}";
            await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");

            var now = time.UtcNow();
            var cooldownStart = now.AddSeconds(-ResendSeconds);
            if (await db.PlayerEmailCodes.AnyAsync(c => c.PlayerId == playerId && c.Purpose == purpose && c.CreatedAt > cooldownStart))
                return (null, CodeRefusal.TooSoon);

            // old codes are only marked used, never deleted, so they still count toward the day
            var dayStart = now.AddHours(-24);
            if (await db.PlayerEmailCodes.CountAsync(c => c.PlayerId == playerId && c.Purpose == purpose && c.CreatedAt > dayStart) >= RateLimits.CodesPerDay)
                return (null, CodeRefusal.TooManyToday);

            await db.PlayerEmailCodes
                .Where(c => c.PlayerId == playerId && c.Purpose == purpose && c.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now));

            var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
            db.PlayerEmailCodes.Add(new PlayerEmailCode
            {
                PlayerId = playerId,
                Purpose = purpose,
                CodeHash = HashCode(playerId, purpose, code),
                ExpiresAt = now.AddMinutes(CodeMinutes),
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return (code, CodeRefusal.None);
        }

        // redeems a code and then runs whatever the code is for (set the password, mark the email verified)
        // in the same transaction. if that follow-up throws, everything rolls back and the code is still
        // good, instead of being used up for nothing.
        // careful: a wrong guess has to keep counting against the attempt cap, so that path commits
        // instead of rolling back. a rollback there would quietly undo the brute-force limit
        public async Task<bool> RedeemCodeThen(Guid playerId, EmailCodePurpose purpose, string submitted, Func<Task> whenRedeemed)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var redeemed = await RedeemCode(playerId, purpose, submitted);
            if (redeemed) await whenRedeemed();
            await transaction.CommitAsync();
            return redeemed;
        }

        // checks a submitted code against the player's newest live one and uses it up if it matches.
        // the attempt is counted first, in the db, so parallel guesses can't get past the cap,
        // and only one request can ever redeem a code. private so it's only used through RedeemCodeThen
        async Task<bool> RedeemCode(Guid playerId, EmailCodePurpose purpose, string submitted)
        {
            var now = time.UtcNow();
            var entry = await db.PlayerEmailCodes
                .Where(c => c.PlayerId == playerId && c.Purpose == purpose
                    && c.UsedAt == null && c.ExpiresAt > now && c.Attempts < MaxCodeAttempts)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();
            if (entry is null) return false;

            var counted = await db.PlayerEmailCodes
                .Where(c => c.Id == entry.Id && c.Attempts < MaxCodeAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1));
            if (counted == 0) return false;

            if (!CryptographicOperations.FixedTimeEquals(entry.CodeHash, HashCode(playerId, purpose, submitted)))
                return false;

            var used = await db.PlayerEmailCodes
                .Where(c => c.Id == entry.Id && c.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now));
            return used == 1;
        }

        // takes the sender as a parameter and touches nothing else, so it stays static. the forgot-password
        // route also calls it from a background task, where a request-scoped service wouldn't be safe
        internal static Task SendCodeMail(IEmailSender mail, string to, EmailCodePurpose purpose, string code)
        {
            var (subject, what) = purpose == EmailCodePurpose.Verify
                ? ("Verify your email", "verify your email address")
                : ("Reset your password", "reset your password");
            return mail.SendAsync(to, $"Hakutaku: {subject}",
                $"Your code to {what} is {code}.\n\nIt expires in {CodeMinutes} minutes. " +
                "If you didn't ask for this, ignore this email; nothing changes unless the code is entered.");
        }
    }
}
