using server.Constants;

namespace server.Auth
{
    // the "any signed-in admin" check, as something you chain onto a route. the owner-only check
    // lives in AdminSessionService, since a handler calls it and it needs the db
    public static class AdminGuards
    {
        // chain it on a route like .RequireRateLimiting(): app.MapGet(...).RequireAdmin()
        // lets in any signed-in admin or the owner. if only the owner should get in, call
        // RequireOwner inside the handler instead
        public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        {
            builder.AddEndpointFilter(async (context, next) =>
            {
                var sessions = context.HttpContext.RequestServices.GetRequiredService<AdminSessionService>();
                var found = await sessions.FindActiveSession(context.HttpContext);
                if (found is null)
                    return Results.Json(new { error = ErrorMessages.NotLoggedIn }, statusCode: StatusCodes.Status401Unauthorized);
                return await next(context);
            });
            return builder;
        }
    }
}
