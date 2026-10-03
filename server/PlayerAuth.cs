using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using server.Models;

namespace server
{
    public record PlayerRegisterRequest(string? DeviceId, string? DisplayName);
    public record PlayerEmailRequest(string? Email, string? Password);
    public record AdminCreatePlayerRequest(string? DeviceId, int Xp, string? DisplayName);
    public record PlayerDisplayNameRequest(string? DisplayName);
    public record PlayerCodeRequest(string? Code);
    public record PlayerForgotRequest(string? Email);
    public record PlayerResetRequest(string? Email, string? Code, string? NewPassword);

    // Player-facing auth. Public routes, so each one is rate limited and has its own
    // handle block in caddy/Caddyfile. Sessions are a Bearer token rather than a
    // cookie: the callers are game clients, not browsers.
    public static class PlayerAuth
    {
        // The TDD gives player_sessions an expiry but no idle column, so this is a
        // single fixed lifetime, not the admin session's idle + absolute pair.
        const int SessionDays = 30;

        public static string ProviderName(PlayerProvider provider) => provider.ToString().ToLowerInvariant();

        internal const string DisplayNameRule = "displayName must be 3-25 characters, with no control characters";

        // A display name is a label, not an identity -- not unique and never used to sign
        // in -- so the only rules are about what can be shown: 3-25 characters once trimmed
        // (PlayFab's limits) and no control characters, which could break a UI or a log
        // line. Null means none was given, which is allowed; whitespace alone is too short.
        internal static bool TryNormalizeDisplayName(string? raw, out string? displayName)
        {
            displayName = raw?.Trim();
            return displayName is null
                || (displayName.Length is >= 3 and <= 25 && !displayName.Any(char.IsControl));
        }

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

        const int CodeMinutes = 15;
        // Six digits is only a million guesses, so a code dies after this many wrong tries.
        const int MaxCodeAttempts = 5;
        // Minimum gap between mails to one player for the same purpose, so the endpoints
        // can't be used to flood someone's inbox.
        const int ResendSeconds = 60;

        static byte[] HashCode(Guid playerId, EmailCodePurpose purpose, string code) =>
            AdminAuth.HashToken($"{playerId}:{(short)purpose}:{code}");

        static bool LooksLikeCode(string? code) =>
            code is { Length: 6 } && code.All(char.IsAsciiDigit);

        // Makes a fresh code and retires any older unused one. Returns null, creating
        // nothing, if one was already made in the last ResendSeconds.
        static async Task<string?> CreateCode(Db db, Guid playerId, EmailCodePurpose purpose)
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

        // Checks a submitted code against the player's newest live one and uses it up if
        // it matches. The attempt is counted first, in the database, so parallel guesses
        // can't get past the cap, and only one request can ever redeem a code.
        static async Task<bool> RedeemCode(Db db, Guid playerId, EmailCodePurpose purpose, string submitted)
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

        static Task SendCodeMail(IEmailSender mail, string to, EmailCodePurpose purpose, string code)
        {
            var (subject, what) = purpose == EmailCodePurpose.Verify
                ? ("Verify your email", "verify your email address")
                : ("Reset your password", "reset your password");
            return mail.SendAsync(to, $"Hakutaku: {subject}",
                $"Your code to {what} is {code}.\n\nIt expires in {CodeMinutes} minutes. " +
                "If you didn't ask for this, ignore this email; nothing changes unless the code is entered.");
        }

        // The email identity of the player a Bearer token belongs to, or null.
        static Task<PlayerIdentity?> FindEmailIdentity(Db db, Guid playerId) =>
            db.PlayerIdentities.FirstOrDefaultAsync(i => i.PlayerId == playerId && i.Provider == PlayerProvider.Email);

        public static void MapPlayerEndpoints(this WebApplication app)
        {
            // Find-or-create by device ID, then start a session. The device UUID is made by
            // the game and kept on the device, so it works as a password nobody types:
            // whoever holds it is that player.
            // displayName is optional and only used when this call creates the player; for a
            // known device it is checked but ignored, and the stored name is returned so the
            // game can see what stuck. Changing it later is /api/players/display-name.
            app.MapPost("/api/players/register", async (Db db, PlayerRegisterRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 254)
                    return Results.BadRequest(new { error = "deviceId is required" });
                if (!TryNormalizeDisplayName(request.DisplayName, out var displayName))
                    return Results.BadRequest(new { error = DisplayNameRule });

                var deviceId = request.DeviceId.Trim();
                var player = await FindByIdentity(db, PlayerProvider.Device, deviceId);
                if (player is null)
                {
                    player = new Player { DisplayName = displayName };
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
                return Results.Ok(new { id = player.Id, deviceId, displayName = player.DisplayName, xp = player.Xp, token, expiresAt });
            }).RequireRateLimiting("player-register");

            // Needs the player token. Sets or replaces the display name. Not rate limited, for
            // the same reason as link/email: it needs a session, and it is one cheap UPDATE.
            app.MapPost("/api/players/display-name", async (Db db, HttpContext http, PlayerDisplayNameRequest request) =>
            {
                var player = await FindBySession(db, http);
                if (player is null)
                    return Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized);

                if (request.DisplayName is null || !TryNormalizeDisplayName(request.DisplayName, out var displayName))
                    return Results.BadRequest(new { error = DisplayNameRule });

                await db.Players
                    .Where(p => p.Id == player.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, displayName));
                return Results.Ok(new { displayName });
            });

            // Needs a player session. Adds an email identity next to the device one; it
            // never replaces it. Deliberately not rate limited: it is gated by a session,
            // and the cheap existence checks run before Argon2, so the expensive hash
            // happens at most once per session.
            app.MapPost("/api/players/link/email", async (Db db, HttpContext http, IEmailSender mail, ILoggerFactory logs, PlayerEmailRequest request) =>
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

                // The link is already saved, so a mail failure must not undo it; the player
                // can ask for another code with /email/resend.
                var sent = false;
                try
                {
                    var code = await CreateCode(db, player.Id, EmailCodePurpose.Verify);
                    if (code is not null)
                    {
                        await SendCodeMail(mail, email, EmailCodePurpose.Verify, code);
                        sent = true;
                    }
                }
                catch (Exception e)
                {
                    logs.CreateLogger("PlayerAuth").LogError(e, "Could not send the verification email");
                }

                return Results.Ok(new { provider = ProviderName(PlayerProvider.Email), email, verified = false, verificationSent = sent });
            });

            // Needs the player token. Redeems the code mailed by link/email or /email/resend.
            app.MapPost("/api/players/email/verify", async (Db db, HttpContext http, PlayerCodeRequest request) =>
            {
                var player = await FindBySession(db, http);
                if (player is null)
                    return Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized);

                var identity = await FindEmailIdentity(db, player.Id);
                if (identity is null)
                    return Results.BadRequest(new { error = "no email is linked to this player" });
                if (identity.VerifiedAt is not null)
                    return Results.Ok(new { verified = true });

                var code = request.Code?.Trim();
                if (!LooksLikeCode(code) || !await RedeemCode(db, player.Id, EmailCodePurpose.Verify, code!))
                    return Results.BadRequest(new { error = "invalid or expired code" });

                var now = DateTime.UtcNow;
                await db.PlayerIdentities
                    .Where(i => i.PlayerId == player.Id && i.Provider == PlayerProvider.Email)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.VerifiedAt, now));
                return Results.Ok(new { verified = true });
            }).RequireRateLimiting("player-mail");

            // Needs the player token. Mails a new code, at most once a minute per player.
            app.MapPost("/api/players/email/resend", async (Db db, HttpContext http, IEmailSender mail, ILoggerFactory logs) =>
            {
                var player = await FindBySession(db, http);
                if (player is null)
                    return Results.Json(new { error = "not logged in" }, statusCode: StatusCodes.Status401Unauthorized);

                var identity = await FindEmailIdentity(db, player.Id);
                if (identity is null)
                    return Results.BadRequest(new { error = "no email is linked to this player" });
                if (identity.VerifiedAt is not null)
                    return Results.Json(new { error = "email is already verified" }, statusCode: StatusCodes.Status409Conflict);

                var code = await CreateCode(db, player.Id, EmailCodePurpose.Verify);
                if (code is null)
                    return Results.Json(new { error = $"wait {ResendSeconds} seconds between codes" }, statusCode: StatusCodes.Status429TooManyRequests);

                try
                {
                    await SendCodeMail(mail, identity.Email!, EmailCodePurpose.Verify, code);
                }
                catch (Exception e)
                {
                    logs.CreateLogger("PlayerAuth").LogError(e, "Could not send the verification email");
                    return Results.Json(new { error = "could not send the email, try again later" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                return Results.Ok(new { sent = true });
            }).RequireRateLimiting("player-mail");

            // Always the same answer, whether or not the email is known and verified, so it
            // can't be used to find out which emails have accounts. The mail goes out in the
            // background so a hit doesn't take noticeably longer than a miss.
            app.MapPost("/api/players/password/forgot", async (Db db, IEmailSender mail, ILoggerFactory logs, PlayerForgotRequest request) =>
            {
                var email = NormalizeEmail(request.Email);
                if (email is null)
                    return Results.BadRequest(new { error = "a valid email is required" });

                var identity = await db.PlayerIdentities.FirstOrDefaultAsync(i =>
                    i.Provider == PlayerProvider.Email && i.Subject == email && i.VerifiedAt != null);
                if (identity is not null)
                {
                    var code = await CreateCode(db, identity.PlayerId, EmailCodePurpose.Reset);
                    if (code is not null)
                    {
                        var log = logs.CreateLogger("PlayerAuth");
                        _ = Task.Run(async () =>
                        {
                            try { await SendCodeMail(mail, email, EmailCodePurpose.Reset, code); }
                            catch (Exception e) { log.LogError(e, "Could not send the password reset email"); }
                        });
                    }
                }
                return Results.Ok(new { ok = true });
            }).RequireRateLimiting("player-mail");

            // Sets a new password if the code mailed by /password/forgot is right. Every
            // session the player has is revoked, since a reset usually means something
            // went wrong; they sign in again (the device ID also still works).
            app.MapPost("/api/players/password/reset", async (Db db, PlayerResetRequest request) =>
            {
                var email = NormalizeEmail(request.Email);
                var code = request.Code?.Trim();
                if (email is null || string.IsNullOrEmpty(request.NewPassword)
                    || request.NewPassword.Length < 8 || request.NewPassword.Length > 256)
                    return Results.BadRequest(new { error = "a valid email is required and the new password must be 8-256 characters" });

                var identity = await db.PlayerIdentities.FirstOrDefaultAsync(i =>
                    i.Provider == PlayerProvider.Email && i.Subject == email && i.VerifiedAt != null);
                if (identity is null || !LooksLikeCode(code) || !await RedeemCode(db, identity.PlayerId, EmailCodePurpose.Reset, code!))
                    return Results.BadRequest(new { error = "invalid or expired code" });

                var hash = AdminAuth.HashPassword(request.NewPassword);
                var now = DateTime.UtcNow;
                await db.PlayerIdentities
                    .Where(i => i.PlayerId == identity.PlayerId && i.Provider == PlayerProvider.Email)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.PwHash, hash));
                await db.PlayerSessions
                    .Where(s => s.PlayerId == identity.PlayerId && s.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
                return Results.Ok(new { ok = true });
            }).RequireRateLimiting("player-mail");

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
                return Results.Ok(new { id = identity.PlayerId, displayName = identity.Player.DisplayName, xp = identity.Player.Xp, token, expiresAt });
            }).RequireRateLimiting("player-login");
        }
    }
}
