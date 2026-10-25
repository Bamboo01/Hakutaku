using server.Constants;

namespace server.Hosting
{
    // Answers for the two cases no route handles itself, in the same { "error": "..." }
    // shape as every other error, so a client only has one shape to parse.
    public static class ApiErrors
    {
        // Any exception a handler doesn't catch becomes a JSON 500. The framework logs the
        // exception itself; the client only gets a generic message, so nothing internal
        // (a SQL error, a stack trace) leaks out. Must be added before the other middleware.
        public static void UseApiExceptionHandler(this WebApplication app)
        {
            app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = ErrorMessages.ServerError });
            }));
        }

        // A path under /api that no route matches gets a JSON 404, not the Vue page. Real
        // routes are more specific, so they win over this catch-all. Without it, the
        // fallback to index.html answers unknown /api paths with a 200 and HTML.
        public static void MapUnknownApiRoutes(this IEndpointRouteBuilder app)
        {
            app.Map("/api/{**path}", () =>
                Results.Json(new { error = ErrorMessages.RouteNotFound }, statusCode: StatusCodes.Status404NotFound));
        }
    }
}
