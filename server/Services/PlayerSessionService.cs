/*!************************************************************************
\file   PlayerSessionService.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines PlayerSessionService, which makes a player session
        at sign-in and finds the player from a Bearer token. Sessions last
        30 days, and only the hash of the token is stored.

        Used by the player auth handlers. Stores PlayerSession rows.
**************************************************************************/
using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Hosting;
using server.Models;

namespace server.Services
{
    // player login sessions: making one at sign-in, and finding the player from a bearer token
    public class PlayerSessionService(Db db, TimeProvider time)
    {
        // one fixed lifetime. player_sessions only has an expiry, no idle column, so there's no
        // idle + absolute pair like the admin sessions have
        const int SessionDays = 30;

        // only the hash is stored, so a token can only be handed out once. that's why every register
        // or login makes a fresh session instead of reusing one
        public async Task<(string Token, DateTime ExpiresAt)> IssueSession(Guid playerId)
        {
            var session = AddSession(playerId);
            await db.SaveChangesAsync();
            return session;
        }

        // adds the session to the db context but doesn't save it, so a caller can save it in one go
        // with other changes (register saves the new player and their first session together)
        public (string Token, DateTime ExpiresAt) AddSession(Guid playerId)
        {
            var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = time.UtcNow().AddDays(SessionDays);
            db.PlayerSessions.Add(new PlayerSession
            {
                PlayerId = playerId,
                TokenHash = TokenHasher.Hash(token),
                ExpiresAt = expiresAt,
            });
            return (token, expiresAt);
        }

        // reads "Authorization: Bearer <token>" and returns the player it belongs to, or null if
        // there's no token or it's unknown, revoked or expired
        public async Task<Player?> FindBySession(HttpContext http)
        {
            var header = http.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var token = header[prefix.Length..].Trim();
            if (token.Length == 0) return null;

            var hash = TokenHasher.Hash(token);
            var now = time.UtcNow();
            return await db.PlayerSessions
                .Where(s => s.TokenHash == hash && s.RevokedAt == null && s.ExpiresAt > now)
                .Join(db.Players, s => s.PlayerId, p => p.Id, (s, p) => p)
                .FirstOrDefaultAsync();
        }
    }
}
