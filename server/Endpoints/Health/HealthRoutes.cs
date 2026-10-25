namespace server.Endpoints.Health
{
    public static class HealthRoutes
    {
        // public through caddy, the jenkins smoke test hits it after every deploy. capital H matters,
        // caddy matches the path exactly. never map "/" to an endpoint: static files skip any request that
        // already matched an endpoint, so it would hide the vue ui
        public static void MapHealthRoutes(this IEndpointRouteBuilder app)
        {
            app.MapGet("/Health", GetHealth.Handle);
        }
    }
}
