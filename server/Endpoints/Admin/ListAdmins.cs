/*!************************************************************************
\file   ListAdmins.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the GET /api/admin/admins route. Owner only. It
        lists every admin, deactivated ones included, because the admins
        page needs their state and their id.

        Mapped in AdminRoutes.cs. Uses AdminSessionService.RequireOwner.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Models;

namespace server.Endpoints.Admin
{
    public static class ListAdmins
    {
        // get /api/admin/admins: owner only. includes deactivated admins, since the admins page needs
        // disabledAt for their state and the id to deactivate someone
        public static async Task<IResult> Handle(Db db, AdminSessionService sessions, HttpContext http)
        {
            var (_, error) = await sessions.RequireOwner(http);
            if (error is not null) return error;

            // load first, shape after, because ef can't turn ToName() into sql. tiny table so it's fine
            var admins = await db.AdminUsers.OrderBy(a => a.Id).ToListAsync();
            return Results.Ok(admins.Select(a => new
            {
                id = a.Id,
                username = a.Username,
                role = a.Role.ToName(),
                createdAt = a.CreatedAt,
                disabledAt = a.DisabledAt,
            }));
        }
    }
}
