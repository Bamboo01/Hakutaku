using server.Constants;

namespace server.Endpoints.Admin
{
    public static class AdminRoutes
    {
        // only file that knows the urls and the rate limit, one line per route. no check on the group
        // because login has to stay open. the others check the session themselves, and the three
        // /admins routes also need the owner
        public static void MapAdminRoutes(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/admin");

            group.MapPost("/login", Login.Handle).RequireRateLimiting(RateLimits.AdminLogin);
            group.MapPost("/logout", Logout.Handle);
            group.MapGet("/me", Me.Handle);

            group.MapGet("/admins", ListAdmins.Handle);
            group.MapPost("/admins", CreateAdmin.Handle);
            group.MapDelete("/admins/{id}", DeactivateAdmin.Handle);
        }
    }
}
