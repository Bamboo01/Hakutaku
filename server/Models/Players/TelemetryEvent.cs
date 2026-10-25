/*!************************************************************************
\file   TelemetryEvent.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the TelemetryEvent model. Telemetry belongs to the
        player and not to a character, and the data is the event's payload
        as JSON text.

        Belongs to a Player. Written by CreateEvent.cs. Table mapped in
        Db.cs.
**************************************************************************/
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
