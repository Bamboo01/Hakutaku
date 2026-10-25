namespace server.Endpoints.Health
{
    public static class GetHealth
    {
        // get /Health: says the app is up, doesn't touch the db
        public static IResult Handle() => Results.Ok(new { health = "ok" });
    }
}
