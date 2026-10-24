using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Services
{
    // Player login sessions: making one at sign-in, and finding the player from a Bearer token.
    internal static class PlayerSessionService
    {
        // One fixed lifetime. The design gives player_sessions an expiry but no idle column,
        // so this is not the admin session's idle + absolute pair.
        const int SessionDays = 30;

        // Only the hash is stored, so a token can be handed out exactly once. That is why
        // every register or login makes a fresh session instead of reusing one.
        internal static async Task<(string Token, DateTime ExpiresAt)> IssueSession(Db db, Guid playerId)
        {
            var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = DateTime.UtcNow.AddDays(SessionDays);
            db.PlayerSessions.Add(new PlayerSession
            {
                PlayerId = playerId,
                TokenHash = AdminAuth.HashToken(token),
                ExpiresAt = expiresAt,
            });
            await db.SaveChangesAsync();
            return (token, expiresAt);
        }

        // Reads "Authorization: Bearer <token>" and returns the player it belongs to, or
        // null if there is no token or it is unknown, revoked or expired.
        internal static async Task<Player?> FindBySession(Db db, HttpContext http)
        {
            var header = http.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var token = header[prefix.Length..].Trim();
            if (token.Length == 0) return null;

            var hash = AdminAuth.HashToken(token);
            var now = DateTime.UtcNow;
            return await db.PlayerSessions
                .Where(s => s.TokenHash == hash && s.RevokedAt == null && s.ExpiresAt > now)
                .Join(db.Players, s => s.PlayerId, p => p.Id, (s, p) => p)
                .FirstOrDefaultAsync();
        }
    }
}
