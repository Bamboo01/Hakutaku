using server.Hosting;
using server.Models;

namespace server.Endpoints.Telemetry
{
    public static class CreateEvent
    {
        // post /api/events: records one event for an existing player. still takes a whole TelemetryEvent
        // entity from the client, a request record replaces that later
        public static async Task<IResult> Handle(Db db, TimeProvider time, TelemetryEvent e)
        {
            // the time is always ours, whatever the client sent
            e.Timestamp = time.UtcNow();
            db.TelemetryEvents.Add(e);
            await db.SaveChangesAsync();
            return Results.Ok(e);
        }
    }
}
