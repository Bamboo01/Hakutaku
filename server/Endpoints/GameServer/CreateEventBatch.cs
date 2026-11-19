/*!************************************************************************
\file   CreateEventBatch.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines the POST /api/server/events/batch route, which
        the C++ ingestion service (ingest/) calls to store telemetry events
        in bulk. Every event is held to the same rules as POST /api/events
        (EventRules); the good ones are stored together in one transaction
        and each bad one is answered by its index, so one broken event can
        never block a whole batch.

        Each event may carry occurredAt, the moment the ingestion service
        received it, so a batch that waited out an API outage keeps its
        real times. It must be within the last 24 hours (a few minutes
        ahead is allowed for clock drift); without it the time is now.

        Mapped in GameServerRoutes.cs, so it needs a server key.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using server.Constants;
using server.Models;
using server.Services;

namespace server.Endpoints.GameServer
{
    public record IncomingEvent(Guid? PlayerId, string? EventType, string? Data, DateTimeOffset? OccurredAt);
    public record EventBatchRequest(List<IncomingEvent?>? Events);
    public record EventRejection(int Index, string Error);

    public static class CreateEventBatch
    {
        // post /api/server/events/batch with { events: [{ playerId, eventType, data, occurredAt? }] }: answers
        // { accepted: n, rejected: [{ index, error }] }. a 400 means the batch itself was bad and nothing was stored
        public static async Task<IResult> Handle(Db db, TimeProvider time, ILoggerFactory logs, EventBatchRequest request)
        {
            if (request.Events is not { Count: > 0 and <= Limits.EventBatchMaxEvents } events)
                return Results.BadRequest(new { error = ErrorMessages.EventBatchInvalid });

            var now = time.GetUtcNow();
            var rejected = new List<EventRejection>();
            var good = new List<(int Index, TelemetryEvent Event)>();

            for (var i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e is null)
                {
                    rejected.Add(new EventRejection(i, ErrorMessages.CreateEventInvalid));
                    continue;
                }
                if (!EventRules.TryRead(e.PlayerId, e.EventType, e.Data, out var eventType, out var error))
                {
                    rejected.Add(new EventRejection(i, error));
                    continue;
                }
                if (e.OccurredAt is { } at && (at < now - Limits.EventMaxAge || at > now + Limits.EventMaxClockSkew))
                {
                    rejected.Add(new EventRejection(i, ErrorMessages.EventTimeInvalid));
                    continue;
                }
                good.Add((i, new TelemetryEvent
                {
                    PlayerId = e.PlayerId!.Value,
                    EventType = eventType,
                    Data = e.Data!,
                    Timestamp = (e.OccurredAt ?? now).UtcDateTime,
                }));
            }

            // one query for every player the batch names, rather than one per event. players are never deleted
            // (there's no route for it), so one that exists now still exists when the insert runs
            var ids = good.Select(g => g.Event.PlayerId).Distinct().ToList();
            var known = (await db.Players.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync()).ToHashSet();

            var keep = new List<TelemetryEvent>();
            foreach (var (index, e) in good)
            {
                if (known.Contains(e.PlayerId)) keep.Add(e);
                else rejected.Add(new EventRejection(index, ErrorMessages.NoSuchPlayer));
            }

            if (keep.Count > 0)
            {
                db.TelemetryEvents.AddRange(keep);
                await db.SaveChangesAsync();
            }

            if (rejected.Count > 0)
                logs.CreateLogger(LogCategories.GameServer).LogWarning(
                    "Event batch: stored {Accepted}, refused {Rejected}", keep.Count, rejected.Count);

            // rejected comes out in index order, whichever check refused each one
            return Results.Ok(new
            {
                accepted = keep.Count,
                rejected = rejected.OrderBy(r => r.Index),
            });
        }
    }
}
