using server.Models;

namespace server.Endpoints.Telemetry
{
    // what the api sends back for an event: just the stored fields, not the whole db entity
    // (which would also carry the nested player)
    public record EventResponse(Guid Id, Guid PlayerId, string EventType, DateTime Timestamp, string Data)
    {
        public static EventResponse From(TelemetryEvent e) => new(e.Id, e.PlayerId, e.EventType, e.Timestamp, e.Data);
    }
}
