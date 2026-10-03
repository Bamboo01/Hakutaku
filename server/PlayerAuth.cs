using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using server.Models;

namespace server
{
    public record PlayerRegisterRequest(string? DeviceId);
    public record PlayerEmailRequest(string? Email, string? Password);
    public record AdminCreatePlayerRequest(string? DeviceId, int Xp);

    // Player-facing auth. Public routes, so each one is rate limited and has its own
    // handle block in caddy/Caddyfile. Sessions are a Bearer token rather than a
    // cookie: the callers are game clients, not browsers.
    public static class PlayerAuth
    {
        // The TDD gives player_sessions an expiry but no idle column, so this is a
        // single fixed lifetime, not the admin session's idle + absolute pair.
        const int SessionDays = 30;

        public static string ProviderName(PlayerProvider provider) => provider.ToString().ToLowerInvariant();

        static bool IsUniqueViolation(DbUpdateException e) =>
            e.InnerException is PostgresException { SqlState: "23505" };

        static Task<Player?> FindByIdentity(Db db, PlayerProvider provider, string subject) =>
            db.PlayerIdentities
                .Where(i => i.Provider == provider && i.Subject == subject)
                .Select(i => i.Player)
                .FirstOrDefaultAsync();

        // Only the hash is stored, so a token can be handed out exactly once -- which is
        // why every register or login creates a fresh session rather than reusing one.
        static async Task<(string Token, DateTime ExpiresAt)> IssueSession(Db db, Guid playerId)
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

        // Reads "Authorization: Bearer <token>" and returns the player it belongs to,
        // or null if there is no token, it is unknown, revoked or expired.
        static async Task<Player?> FindBySession(Db db, HttpContext http)
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

        // No verification mail exists (see TODO.md), so this is only a sanity check
        // that stops obvious typos, not proof the address is real or the caller's.
        static string? NormalizeEmail(string? raw)
        {
            var email = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(email) || email.Length > 254) return null;
            var at = email.IndexOf('@');
            if (at < 1 || at != email.LastIndexOf('@') || at == email.Length - 1) return null;
            return email.Any(char.IsWhiteSpace) ? null : email;
        }

        public static void MapPlayerEndpoints(this WebApplication app)
        {
            // Find-or-create by device ID, then start a session. The device UUID is made by
            // the game and kept on the device, so it works as a password nobody types:
            // whoever holds it is that player.
            app.MapPost("/api/players/register", async (Db db, PlayerRegisterRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 254)
                    return Results.BadRequest(new { error = "deviceId is required" });

                var deviceId = request.DeviceId.Trim();
                var player = await FindByIdentity(db, PlayerProvider.Device, deviceId);
                if (player is null)
                {
                    player = new Player();
                    db.Players.Add(player);
                    db.PlayerIdentities.Add(new PlayerIdentity
                    {
                        Player = player,
                        Provider = PlayerProvider.Device,
                        Subject = deviceId,
                    });
                    try
                    {
                        await db.SaveChangesAsync();
                    }
                    catch (DbUpdateException e) when (IsUniqueViolation(e))
                    {
                        // Lost a race with another registration for the same device between
                        // the lookup and the save. Forget the rows that failed to insert,
                        // or the session save below would retry them, then use the winner.
                        db.ChangeTracker.Clear();
                        player = await FindByIdentity(db, PlayerProvider.Device, deviceId);
                        if (player is null) throw;
                    }
                }

                var (token, expiresAt) = await IssueSession(db, player.Id);
                return Results.Ok(new { id = player.Id, deviceId, xp = player.Xp, token, expiresAt });
            }).RequireRateLimiting("player-register");

            // Needs a player session. Adds an email identity next to the device one; it
            // never replaces it. Deliberately not rate limited: it is gated by a session,
            // and the cheap existence checks run before Argon2, so the expensive hash
            // happens at most once per session.
            app.MapPost("/api/players/link/email", async (Db db, HttpContext http, PlayerEmailRequest request) =>
            {
                var player = await FindBySession(db, http);
                if (player is null)
                    return Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized);

                var email = NormalizeEmail(request.Email);
                if (email is null || string.IsNullOrEmpty(request.Password)
                    || request.Password.Length < 8 || request.Password.Length > 256)
                    return Results.BadRequest(new { error = "a valid email is required and the password must be 8-256 characters" });

                if (await db.PlayerIdentities.AnyAsync(i => i.PlayerId == player.Id && i.Provider == PlayerProvider.Email))
                    return Results.Json(new { error = "this player already has an email linked" }, statusCode: StatusCodes.Status409Conflict);
                if (await db.PlayerIdentities.AnyAsync(i => i.Provider == PlayerProvider.Email && i.Subject == email))
                    return Results.Json(new { error = "email already in use" }, statusCode: StatusCodes.Status409Conflict);

                db.PlayerIdentities.Add(new PlayerIdentity
                {
                    PlayerId = player.Id,
                    Provider = PlayerProvider.Email,
                    Subject = email,
                    Email = email,
                    PwHash = AdminAuth.HashPassword(request.Password),
                });

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException e) when (IsUniqueViolation(e))
                {
                    // Either this player linked a second email at the same moment, or
                    // someone else took this one. The constraint name says which.
                    var oneEmailPerPlayer = e.InnerException is PostgresException { ConstraintName: "ux_player_identities_one_email" };
                    return Results.Json(
                        new { error = oneEmailPerPlayer ? "this player already has an email linked" : "email already in use" },
                        statusCode: StatusCodes.Status409Conflict);
                }

                return Results.Ok(new { provider = ProviderName(PlayerProvider.Email), email });
            });

            // Same answer for an unknown email and a wrong password, and an unknown email
            // still pays for a hash check, so neither the body nor the timing reveals
            // which emails exist -- the same rules as admin login.
            app.MapPost("/api/players/login", async (Db db, PlayerEmailRequest request) =>
            {
                var email = NormalizeEmail(request.Email);
                if (email is null || string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
                    return Results.BadRequest(new { error = "email and password are required" });

                var identity = await db.PlayerIdentities
                    .Include(i => i.Player)
                    .FirstOrDefaultAsync(i => i.Provider == PlayerProvider.Email && i.Subject == email);

                var passwordOk = AdminAuth.VerifyPassword(identity?.PwHash ?? AdminAuth.DummyHash, request.Password);
                if (identity is null || !passwordOk)
                    return Results.Json(new { error = "invalid credentials" }, statusCode: StatusCodes.Status401Unauthorized);

                var (token, expiresAt) = await IssueSession(db, identity.PlayerId);
                return Results.Ok(new { id = identity.PlayerId, xp = identity.Player.Xp, token, expiresAt });
            }).RequireRateLimiting("player-login");
        }
    }
}
