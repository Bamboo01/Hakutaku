/*!************************************************************************
\file   AdminGuards.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines RequireAdmin, an extension that is chained onto a
        route or a route group to let in any signed-in admin or the owner.
        Owner-only routes call RequireOwner inside the handler instead.

        Used by CharacterRoutes.cs, PlayerRoutes.cs and TelemetryRoutes.cs.
        Looks the session up through AdminSessionService.
**************************************************************************/
using server.Constants;

namespace server.Auth
{
    // the "any signed-in admin" check, as something you chain onto a route. the owner-only check
    // lives in AdminSessionService, since a handler calls it and it needs the db
    public static class AdminGuards
    {
        // where the check leaves the username of the admin who made the call, in HttpContext.Items, so a
        // handler can say who did something in a log line without looking the session up again
        public const string ItemKey = "AdminUsername";

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
                context.HttpContext.Items[ItemKey] = found.Value.Admin.Username;
                return await next(context);
            });
            return builder;
        }
    }
}
