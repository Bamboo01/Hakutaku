using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Hosting;
using server.Models;

namespace server.Auth
{
    // admin login sessions: the cookie settings, finding which session a request belongs to, and the
    // owner check. the lookups use the db and the clock so they need an instance. the settings are
    // plain constants and one pure function, so those stay static
    public class AdminSessionService(Db db, TimeProvider time)
    {
        internal const string CookieName = "hakutaku_admin_session";

        // hard limit: the session dies here no matter how active it is
        internal const int AbsoluteHours = 12;
        // idle limit: every request pushes it forward (never past the hard limit), so a forgotten tab dies on its own
        internal const int IdleMinutes = 30;

        internal static CookieOptions SessionCookieOptions(HttpRequest request, DateTime expires) => new()
        {
            HttpOnly = true,
            // only marked secure when the request came in over https, so local dev (plain http) still works
            // in prod UseForwardedHeaders() reports the https that caddy terminated
            Secure = request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/",
        };

        // finds the session for the cookie and checks it's still good: not revoked, not past either expiry,
        // admin not disabled. a successful call also pushes the idle expiry forward, so callers get
        // "activity keeps you logged in" for free
        public async Task<(AdminSession Session, AdminUser Admin)?> FindActiveSession(HttpContext http)
        {
            if (!http.Request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrEmpty(token))
                return null;

            var tokenHash = TokenHasher.Hash(token);
            var now = time.UtcNow();
            var session = await db.AdminSessions.FirstOrDefaultAsync(s =>
                s.TokenHash == tokenHash
                && s.RevokedAt == null
                && s.ExpiresAt > now
                && s.IdleExpiresAt > now);
            if (session is null) return null;

            var admin = await db.AdminUsers.FindAsync(session.AdminId);
            if (admin is null || admin.DisabledAt is not null) return null;

            var nextIdle = now.AddMinutes(IdleMinutes);
            session.IdleExpiresAt = nextIdle < session.ExpiresAt ? nextIdle : session.ExpiresAt;
            await db.SaveChangesAsync();

            return (session, admin);
        }

        // owner only (not just any admin). per the tdd, making and removing admins goes through the owner
        public async Task<(AdminUser? Owner, IResult? Error)> RequireOwner(HttpContext http)
        {
            var found = await FindActiveSession(http);
            if (found is null)
                return (null, Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized));
            if (found.Value.Admin.Role != AdminRole.Owner)
                return (null, Results.Json(new { error = "owner only" }, statusCode: StatusCodes.Status403Forbidden));
            return (found.Value.Admin, null);
        }
    }
}
