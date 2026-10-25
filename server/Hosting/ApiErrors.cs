using server.Constants;

namespace server.Hosting
{
    // answers for the two cases no route handles, in the same { "error": "..." } shape as every other error
    public static class ApiErrors
    {
        // any exception a handler doesn't catch becomes a json 500. the framework already logs it,
        // the client only gets a generic message so nothing internal leaks (sql errors, stack traces).
        // has to be added before the other middleware
        public static void UseApiExceptionHandler(this WebApplication app)
        {
            app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = ErrorMessages.ServerError });
            }));
        }

        // unknown paths under /api get a json 404 instead of the vue page. real routes are more
        // specific so they still win over this catch-all. without it the index.html fallback
        // answers unknown /api paths with a 200 and html
        public static void MapUnknownApiRoutes(this IEndpointRouteBuilder app)
        {
            app.Map("/api/{**path}", () =>
                Results.Json(new { error = ErrorMessages.RouteNotFound }, statusCode: StatusCodes.Status404NotFound));
        }
    }
}
