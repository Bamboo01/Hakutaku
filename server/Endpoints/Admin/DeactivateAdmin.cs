/*!************************************************************************
\file   DeactivateAdmin.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the DELETE /api/admin/admins/{id} route. Owner
        only. It is one-way: it sets disabled_at instead of deleting the
        row, and revokes the admin's sessions in the same transaction so it
        takes effect at once. The owner can never be deactivated.

        Mapped in AdminRoutes.cs. Uses AdminSessionService.RequireOwner.
**************************************************************************/
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

            // disabling the admin and revoking their sessions go together, so a failure can't leave
            // the sessions dead but the account still able to log back in
            var now = time.UtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync();
            target.DisabledAt = now;
            await db.AdminSessions
                .Where(s => s.AdminId == target.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.NoContent();
        }
    }
}
