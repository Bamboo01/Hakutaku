using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Hosting;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class DeactivateAdmin
    {
        // delete /api/admin/admins/{id}: owner only. one-way, it sets disabled_at and never deletes the
        // row, no restore. also revokes their sessions so it takes effect now, not when the cookie runs out
        public static async Task<IResult> Handle(Db db, AdminSessionService sessions, TimeProvider time, HttpContext http, long id)
        {
            var (owner, error) = await sessions.RequireOwner(http);
            if (error is not null) return error;

            var target = await db.AdminUsers.FindAsync(id);
            if (target is null)
                return Results.NotFound(new { error = "no such admin" });
            if (target.Role == AdminRole.Owner)
                return Results.Json(new { error = "the owner account can't be deactivated" }, statusCode: StatusCodes.Status403Forbidden);

            var now = time.UtcNow();
            target.DisabledAt = now;
            await db.AdminSessions
                .Where(s => s.AdminId == target.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
            await db.SaveChangesAsync();

            return Results.NoContent();
        }
    }
}
