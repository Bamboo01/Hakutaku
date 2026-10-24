using Microsoft.EntityFrameworkCore;
using Npgsql;
using server.Constants;
using server.Models;

namespace server.Endpoints.Admin
{
    public record CreateAdminRequest(string? Username, string? Password);

    public static class CreateAdmin
    {
        // POST /api/admin/admins: owner only. Always makes a regular admin. There is only
        // ever one owner, the one created at first start.
        public static async Task<IResult> Handle(Db db, HttpContext http, CreateAdminRequest request)
        {
            var (owner, error) = await AdminAuth.RequireOwner(db, http);
            if (error is not null) return error;

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)
                || request.Username.Length > Limits.MaxIdentifierLength || request.Password.Length < Limits.MinPasswordLength || request.Password.Length > Limits.MaxPasswordLength)
                return Results.BadRequest(new { error = ErrorMessages.CreateAdminInvalid });

            var admin = new AdminUser
            {
                Username = request.Username.Trim(),
                PwHash = AdminAuth.HashPassword(request.Password),
                Role = AdminRole.Admin,
                CreatedBy = owner!.Id,
            };
            db.AdminUsers.Add(admin);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" })
            {
                // A unique violation here means the username is taken (case-insensitively).
                return Results.Json(new { error = "username already taken" }, statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Ok(new { id = admin.Id, username = admin.Username, role = AdminAuth.RoleName(admin.Role) });
        }
    }
}
