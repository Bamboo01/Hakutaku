using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Services
{
    // The 6-digit codes mailed for verifying an email and for resetting a password.
    internal static class EmailCodeService
    {
        const int CodeMinutes = 15;

        // Six digits is only a million guesses, so a code dies after this many wrong tries.
        const int MaxCodeAttempts = 5;

        // Minimum gap between mails to one player for the same purpose, so the routes
        // can't be used to flood someone's inbox.
        internal const int ResendSeconds = 60;

        // Only a hash of the code is stored, tied to the player and what it is for.
        static byte[] HashCode(Guid playerId, EmailCodePurpose purpose, string code) =>
            AdminAuth.HashToken($"{playerId}:{(short)purpose}:{code}");

        internal static bool LooksLikeCode(string? code) =>
            code is { Length: 6 } && code.All(char.IsAsciiDigit);

        // Makes a fresh code and retires any older unused one. Returns null, creating
        // nothing, if one was already made in the last ResendSeconds.
        internal static async Task<string?> CreateCode(Db db, Guid playerId, EmailCodePurpose purpose)
        {
            var now = DateTime.UtcNow;
            var cooldownStart = now.AddSeconds(-ResendSeconds);
            if (await db.PlayerEmailCodes.AnyAsync(c => c.PlayerId == playerId && c.Purpose == purpose && c.CreatedAt > cooldownStart))
                return null;

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
            return code;
        }

        // Checks a submitted code against the player's newest live one and uses it up if it
        // matches. The attempt is counted first, in the database, so parallel guesses can't
        // get past the cap, and only one request can ever redeem a code.
        internal static async Task<bool> RedeemCode(Db db, Guid playerId, EmailCodePurpose purpose, string submitted)
        {
            var now = DateTime.UtcNow;
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
