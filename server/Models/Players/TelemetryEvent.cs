namespace server.Models
{
    // Telemetry is player-scoped, not character-scoped (see CLAUDE.md).
    // Data holds the event-specific payload as JSON.
    public class TelemetryEvent
    {
        public Guid Id { get; set; }
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public string EventType { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string Data { get; set; } = "";
    }
}
