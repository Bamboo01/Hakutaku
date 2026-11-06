/*!************************************************************************
\file   ListServerKeys.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the GET /api/admin/server-keys route. Owner only.
        It lists every server key, revoked and expired ones included, with
        its name and when it was made, expires, was revoked and was last
        used. The key itself is never in the list, since only its hash is
        stored.

        Mapped in AdminRoutes.cs. Uses AdminSessionService.RequireOwner.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class ListServerKeys
    {
        // get /api/admin/server-keys: owner only. the keys page works out each key's state from these dates
        // and needs the id to revoke one
        public static async Task<IResult> Handle(Db db, AdminSessionService sessions, HttpContext http)
        {
            var (_, error) = await sessions.RequireOwner(http);
            if (error is not null) return error;

            var keys = await db.ServerKeys
                .OrderBy(k => k.Id)
                .Select(k => new
                {
                    id = k.Id,
                    name = k.Name,
                    createdAt = k.CreatedAt,
                    expiresAt = k.ExpiresAt,
                    revokedAt = k.RevokedAt,
                    lastUsedAt = k.LastUsedAt,
                })
                .ToListAsync();
            return Results.Ok(keys);
        }
    }
}
