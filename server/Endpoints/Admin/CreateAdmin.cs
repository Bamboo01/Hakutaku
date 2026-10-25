/*!************************************************************************
\file   CreateAdmin.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the POST /api/admin/admins route. Owner only. It
        always creates a regular admin, since there is only one owner and it
        is made at first start. The request is a small record, so a client
        can only send a username and password.

        Mapped in AdminRoutes.cs. Uses AdminSessionService.RequireOwner and
        PasswordHasher.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Auth;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.Admin
{
    public record CreateAdminRequest(string? Username, string? Password);

    public static class CreateAdmin
    {
        // post /api/admin/admins: owner only. always makes a regular admin, there's only one owner
        // (made at first start)
        public static async Task<IResult> Handle(Db db, AdminSessionService sessions, HttpContext http, CreateAdminRequest request)
        {
            var (owner, error) = await sessions.RequireOwner(http);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)
                || request.Username.Length > Limits.MaxIdentifierLength || request.Password.Length < Limits.MinPasswordLength || request.Password.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = ErrorMessages.CreateAdminInvalid });

            var admin = new AdminUser
            {
                Username = request.Username.Trim(),
                PwHash = PasswordHasher.Hash(request.Password),
                Role = AdminRole.Admin,
                CreatedBy = owner!.Id,
            };
            db.AdminUsers.Add(admin);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e))
            {
                // unique violation = username already taken (ignoring case)
                return Results.Json(new { error = "username already taken" }, statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Ok(new { id = admin.Id, username = admin.Username, role = admin.Role.ToName() });
        }
    }
}
