namespace server.Models
{
    // telemetry belongs to the player, not a character (see CLAUDE.md). Data is the event's payload as json
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
