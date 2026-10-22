using server.Models;

namespace server.Endpoints.Telemetry
{
    public static class CreateEvent
    {
        // POST /api/events: records one event for an existing player.
        // The client still sends a whole TelemetryEvent entity; a request record replaces that later.
        public static async Task<IResult> Handle(Db db, TelemetryEvent e)
        {
            // The time is always ours, whatever the client sent.
            e.Timestamp = DateTime.UtcNow;
            db.TelemetryEvents.Add(e);
            await db.SaveChangesAsync();
            return Results.Ok(e);
        }
    }
}
