using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class DeactivateAdmin
    {
        // DELETE /api/admin/admins/{id}: owner only. One-way: it sets disabled_at and never
        // deletes the row, and there is no restore. It also revokes that admin's sessions,
        // so the deactivation takes effect now instead of when their cookie expires.
        public static async Task<IResult> Handle(Db db, HttpContext http, long id)
        {
            var (owner, error) = await AdminAuth.RequireOwner(db, http);
            if (error is not null) return error;

            var target = await db.AdminUsers.FindAsync(id);
            if (target is null)
                return Results.NotFound(new { error = "no such admin" });
            if (target.Role == AdminRole.Owner)
                return Results.Json(new { error = "the owner account can't be deactivated" }, statusCode: StatusCodes.Status403Forbidden);

            var now = DateTime.UtcNow;
            target.DisabledAt = now;
            await db.AdminSessions
                .Where(s => s.AdminId == target.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
            await db.SaveChangesAsync();

            return Results.NoContent();
        }
    }
}
