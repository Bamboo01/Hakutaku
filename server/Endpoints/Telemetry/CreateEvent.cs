using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Hosting;
using server.Models;
using server.Services;

namespace server.Endpoints.Telemetry
{
    // only the fields the client is allowed to send. the id and the time are always the server's,
    // and the player is just an id, so there's no way to slip in a new player
    public record CreateEventRequest(Guid? PlayerId, string? EventType, string? Data);

    public static class CreateEvent
    {
        // data is json kept as text. we only check it parses, we don't look inside it
        static bool IsValidJson(string text)
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

        // post /api/events: records one event for an existing player
        public static async Task<IResult> Handle(Db db, TimeProvider time, CreateEventRequest request)
        {
            var eventType = request.EventType?.Trim();
            if (request.PlayerId is null || request.PlayerId == Guid.Empty
                || string.IsNullOrEmpty(eventType) || eventType.Length > Limits.EventTypeMaxLength || eventType.Any(char.IsControl))
                return Results.BadRequest(new { error = ErrorMessages.CreateEventInvalid });

            if (string.IsNullOrWhiteSpace(request.Data)
                || Encoding.UTF8.GetByteCount(request.Data) > Limits.EventDataMaxBytes
                || !IsValidJson(request.Data))
                return Results.BadRequest(new { error = ErrorMessages.EventDataInvalid });

            var telemetryEvent = new TelemetryEvent
            {
                PlayerId = request.PlayerId.Value,
                EventType = eventType,
                Data = request.Data,
                // the time is always ours, whatever the client sent
                Timestamp = time.UtcNow(),
            };
            db.TelemetryEvents.Add(telemetryEvent);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (DbErrors.IsForeignKeyViolation(e))
            {
                // no player has that id (see CreateCharacter)
                return Results.BadRequest(new { error = ErrorMessages.NoSuchPlayer });
            }

            return Results.Ok(EventResponse.From(telemetryEvent));
        }
    }
}
