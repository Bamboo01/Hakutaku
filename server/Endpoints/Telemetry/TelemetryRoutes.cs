namespace server.Endpoints.Telemetry
{
    public static class TelemetryRoutes
    {
        // The only file that knows the URLs and who may call them: one line per route.
        public static void MapTelemetryRoutes(this IEndpointRouteBuilder app)
        {
            // Admin-only for now, so the check goes on the whole group instead of each route.
            var group = app.MapGroup("/api/events").RequireAdmin();

            group.MapGet("", ListEvents.Handle);
            group.MapPost("", CreateEvent.Handle);
        }
    }
}
