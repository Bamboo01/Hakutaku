/*!************************************************************************
\file   EventRules.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines EventRules, the checks a telemetry event has to
        pass before it is stored: a player id, an event type of 1-100
        characters with no control characters, and data that is valid JSON
        of at most 10,000 bytes. Plain checks with no database, so they are
        easy to test on their own.

        Used by the admin's CreateEvent.cs and the ingestion service's
        CreateEventBatch.cs, so an event is held to the same rules whichever
        way it arrives. The numbers come from Limits.cs.
**************************************************************************/
using System.Text;
using System.Text.Json;
using server.Constants;

namespace server.Services
{
    internal static class EventRules
    {
        // checks one event. on success eventType is trimmed; on a problem, error is the message to answer with
        internal static bool TryRead(Guid? playerId, string? rawType, string? data, out string eventType, out string error)
        {
            eventType = rawType?.Trim() ?? "";
            error = "";
            if (playerId is null || playerId == Guid.Empty
                || eventType.Length == 0 || eventType.Length > Limits.EventTypeMaxLength || eventType.Any(char.IsControl))
            {
                error = ErrorMessages.CreateEventInvalid;
                return false;
            }
            if (string.IsNullOrWhiteSpace(data) || Encoding.UTF8.GetByteCount(data) > Limits.EventDataMaxBytes || !IsValidJson(data))
            {
                error = ErrorMessages.EventDataInvalid;
                return false;
            }
            return true;
        }

        // data is json kept as text. we only check it parses, we don't look inside it
        internal static bool IsValidJson(string text)
        {
            try
            {
                using var _ = JsonDocument.Parse(text);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
