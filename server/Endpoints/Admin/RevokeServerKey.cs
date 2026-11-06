/*!************************************************************************
\file   RevokeServerKey.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the DELETE /api/admin/server-keys/{id} route. Owner
        only. It sets revoked_at on the key, so it stops working at once.
        It is one-way and the row is kept, like deactivating an admin.
        Revoking a key that is already revoked changes nothing.

        Mapped in AdminRoutes.cs. Uses AdminSessionService.RequireOwner.
**************************************************************************/
using server.Auth;
using server.Hosting;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class RevokeServerKey
    {
        // delete /api/admin/server-keys/{id}: owner only. one-way, no restore. a key that's already revoked
        // keeps its first revoked_at
        public static async Task<IResult> Handle(Db db, AdminSessionService sessions, TimeProvider time, HttpContext http, long id)
        {
            var (_, error) = await sessions.RequireOwner(http);
            if (error is not null) return error;

            var key = await db.ServerKeys.FindAsync(id);
            if (key is null)
                return Results.NotFound(new { error = "no such server key" });

            if (key.RevokedAt is null)
            {
                key.RevokedAt = time.UtcNow();
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        }
    }
}
