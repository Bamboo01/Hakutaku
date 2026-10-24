using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class ListAdmins
    {
        // GET /api/admin/admins: owner only. Includes deactivated admins, because the admins
        // page needs disabledAt to show their state and the id to target a deactivation.
        public static async Task<IResult> Handle(Db db, HttpContext http)
        {
            var (_, error) = await AdminAuth.RequireOwner(db, http);
            if (error is not null) return error;

            // Loaded first and shaped afterwards, because EF can't turn RoleName into SQL.
            // The table is tiny, so this is fine.
            var admins = await db.AdminUsers.OrderBy(a => a.Id).ToListAsync();
            return Results.Ok(admins.Select(a => new
            {
                id = a.Id,
                username = a.Username,
                role = AdminAuth.RoleName(a.Role),
                createdAt = a.CreatedAt,
                disabledAt = a.DisabledAt,
            }));
        }
    }
}
