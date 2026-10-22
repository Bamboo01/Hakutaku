using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Endpoints.Telemetry
{
    public static class ListEvents
    {
        // GET /api/events: every telemetry event. No paging yet.
        public static async Task<IResult> Handle(Db db) =>
            Results.Ok(await db.TelemetryEvents.ToListAsync());
    }
}
