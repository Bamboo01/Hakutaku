using server.Auth;

namespace server.Endpoints.Telemetry
{
    public static class TelemetryRoutes
    {
        // only file that knows the urls and who can call them, one line per route
        public static void MapTelemetryRoutes(this IEndpointRouteBuilder app)
        {
            // admin only for now, so the check goes on the whole group and not each route
            var group = app.MapGroup("/api/events").RequireAdmin();

            group.MapGet("", ListEvents.Handle);
            group.MapPost("", CreateEvent.Handle);
        }
    }
}
