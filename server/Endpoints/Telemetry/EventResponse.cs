/*!************************************************************************
\file   EventResponse.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines EventResponse, the small record the API sends back
        for a telemetry event, so the whole database entity (with its nested
        player) never leaves the server.

        Used by CreateEvent.cs and ListEvents.cs. Built from the
        TelemetryEvent model.
**************************************************************************/
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
