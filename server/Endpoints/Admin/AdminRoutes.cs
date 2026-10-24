using server.Constants;

namespace server.Endpoints.Admin
{
    public static class AdminRoutes
    {
        // The only file that knows the URLs and the rate limit: one line per route.
        // There is no check on the group, because login must stay open. The other routes
        // check the session themselves, and the three /admins routes also require the owner.
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
