// This is just sample code, feel free to overwrite it.

using Microsoft.EntityFrameworkCore;

namespace server.Models
{
    // Create a POD struct
    public class Player
    {
        public Guid Id { get; set; }
        public string DeviceId { get; set; } = "";
        public int Xp { get; set; }
    }

    // A Player can have multiple characters. No independent progress/stats of
    // their own yet -- that's tracked on Player -- so this is just identity.
    public class Character
    {
        public Guid Id { get; set; }
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public string Name { get; set; } = "";
    }

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

    public class Db : DbContext
    {
        public Db(DbContextOptions<Db> options) : base(options) { }

        // C# magic, serializes a POD struct into a database set
        public DbSet<Player> Players => Set<Player>();
        public DbSet<Character> Characters => Set<Character>();
        public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();
    }
}
