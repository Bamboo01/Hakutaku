/*!************************************************************************
\file   EventBatchTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   17th November 2026
\brief  This file defines the integration tests for POST
        /api/server/events/batch, which the C++ ingestion service calls. A
        batch is stored in one go; a bad event is refused by its index while
        the rest are stored; an unknown player is refused per event; the
        batch size and occurredAt are bounded; occurredAt is kept as the
        event's time; and only a server key gets in.

        Runs against the real server through HakutakuFactory. Tests
        CreateEventBatch.cs and EventRules.cs.
**************************************************************************/
using System.Net;
using System.Text.Json;
using server.Constants;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class EventBatchTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>, IAsyncLifetime
    {
        const string Path = "/api/server/events/batch";
        readonly ApiClient api = app.Api();
        string key = "";

        public async Task InitializeAsync()
        {
            var owner = app.Api();
            await owner.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            var made = await owner.Post("/api/admin/server-keys", new { name = "ingest-" + Guid.NewGuid().ToString("N")[..8] });
            key = made.Json.GetProperty("key").GetString()!;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        Task<Reply> Send(params object?[] events) => api.Post(Path, new { events }, serverKey: key);

        static object Event(Guid player, string type = "level_up", string data = """{"level":2}""", DateTimeOffset? occurredAt = null) =>
            new { playerId = player, eventType = type, data, occurredAt };

        Task<long> Stored(Guid player) => app.Scalar<long>($"select count(*) from \"TelemetryEvents\" where \"PlayerId\" = '{player}'");

        static List<(int Index, string Error)> Rejected(Reply reply) =>
            reply.Json.GetProperty("rejected").EnumerateArray()
                .Select(r => (r.GetProperty("index").GetInt32(), r.GetProperty("error").GetString()!)).ToList();

        [Fact]
        public async Task A_whole_batch_is_stored_and_counted()
        {
            var player = (await api.RegisterGuest()).Id;

            var reply = await Send(Event(player), Event(player, "match_end", """{"won":true}"""), Event(player));

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(3, reply.Json.GetProperty("accepted").GetInt32());
            Assert.Empty(Rejected(reply));
            Assert.Equal(3, await Stored(player));
        }

        [Fact]
        public async Task A_bad_event_is_refused_by_its_index_and_the_rest_are_still_stored()
        {
            var player = (await api.RegisterGuest()).Id;

            var reply = await Send(
                Event(player),
                Event(player, data: "not json"),
                Event(player, type: ""),
                null,
                Event(player, data: new string('x', Limits.EventDataMaxBytes)),
                Event(player));

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(2, reply.Json.GetProperty("accepted").GetInt32());
            Assert.Equal(
                [(1, ErrorMessages.EventDataInvalid), (2, ErrorMessages.CreateEventInvalid), (3, ErrorMessages.CreateEventInvalid), (4, ErrorMessages.EventDataInvalid)],
                Rejected(reply));
            Assert.Equal(2, await Stored(player));
        }

        [Fact]
        public async Task An_event_for_a_player_that_does_not_exist_is_refused_on_its_own()
        {
            var player = (await api.RegisterGuest()).Id;
            var nobody = Guid.NewGuid();

            var reply = await Send(Event(nobody), Event(player));

            Assert.Equal(1, reply.Json.GetProperty("accepted").GetInt32());
            Assert.Equal([(0, ErrorMessages.NoSuchPlayer)], Rejected(reply));
            Assert.Equal(0, await Stored(nobody));
        }

        [Fact]
        public async Task OccurredAt_is_kept_as_the_events_time()
        {
            var player = (await api.RegisterGuest()).Id;
            var when = DateTimeOffset.UtcNow.AddHours(-3);

            await Send(Event(player, occurredAt: when));

            var stored = await app.Scalar<DateTime>($"select \"Timestamp\" from \"TelemetryEvents\" where \"PlayerId\" = '{player}'");
            Assert.Equal(when.UtcDateTime, stored, TimeSpan.FromMilliseconds(1));
        }

        [Fact]
        public async Task Without_occurredAt_the_time_is_now()
        {
            var player = (await api.RegisterGuest()).Id;

            await Send(Event(player));

            Assert.True(await app.Scalar<bool>($"select \"Timestamp\" > now() - interval '1 minute' from \"TelemetryEvents\" where \"PlayerId\" = '{player}'"));
        }

        [Fact]
        public async Task OccurredAt_too_old_or_too_far_ahead_is_refused_but_a_little_ahead_is_fine()
        {
            var player = (await api.RegisterGuest()).Id;
            var now = DateTimeOffset.UtcNow;

            var reply = await Send(
                Event(player, occurredAt: now.AddHours(-25)),
                Event(player, occurredAt: now.AddMinutes(10)),
                Event(player, occurredAt: now.AddMinutes(1)));

            Assert.Equal(1, reply.Json.GetProperty("accepted").GetInt32());
            Assert.Equal([(0, ErrorMessages.EventTimeInvalid), (1, ErrorMessages.EventTimeInvalid)], Rejected(reply));
        }

        [Fact]
        public async Task An_empty_or_oversized_batch_is_refused_and_stores_nothing()
        {
            var player = (await api.RegisterGuest()).Id;
            var tooMany = Enumerable.Range(0, Limits.EventBatchMaxEvents + 1).Select(_ => Event(player)).ToArray();

            var empty = await api.Post(Path, new { events = Array.Empty<object>() }, serverKey: key);
            var missing = await api.Post(Path, new { }, serverKey: key);
            var over = await Send(tooMany);

            Assert.All([empty, missing, over], r =>
            {
                Assert.Equal(HttpStatusCode.BadRequest, r.Status);
                Assert.Equal(ErrorMessages.EventBatchInvalid, r.Error);
            });
            Assert.Equal(0, await Stored(player));
        }

        [Fact]
        public async Task A_full_batch_of_500_is_stored()
        {
            var player = (await api.RegisterGuest()).Id;
            var full = Enumerable.Range(0, Limits.EventBatchMaxEvents).Select(i => Event(player, data: $$"""{"i":{{i}}}""")).ToArray();

            var reply = await Send(full);

            Assert.Equal(Limits.EventBatchMaxEvents, reply.Json.GetProperty("accepted").GetInt32());
            Assert.Equal(Limits.EventBatchMaxEvents, await Stored(player));
        }

        [Fact]
        public async Task Without_a_server_key_or_with_a_player_token_it_is_401_and_nothing_is_stored()
        {
            var guest = await api.RegisterGuest();
            var body = new { events = new[] { Event(guest.Id) } };

            var none = await api.Post(Path, body);
            var token = await api.Post(Path, body, token: guest.Token);
            var wrong = await api.Post(Path, body, serverKey: "not-a-real-key");

            Assert.All([none, token, wrong], r => Assert.Equal(HttpStatusCode.Unauthorized, r.Status));
            Assert.Equal(0, await Stored(guest.Id));
        }

        [Fact]
        public async Task The_admin_route_still_refuses_what_it_did_and_takes_what_it_did()
        {
            // CreateEvent now uses the shared EventRules, so check nothing changed for it
            var admin = app.Api();
            await admin.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            var player = (await api.RegisterGuest()).Id;

            var ok = await admin.Post("/api/events", new { playerId = player, eventType = "  trimmed  ", data = """{"a":1}""" });
            var badJson = await admin.Post("/api/events", new { playerId = player, eventType = "x", data = "{" });
            var noType = await admin.Post("/api/events", new { playerId = player, eventType = "  ", data = "{}" });

            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal("trimmed", ok.Json.GetProperty("eventType").GetString());
            Assert.Equal(ErrorMessages.EventDataInvalid, badJson.Error);
            Assert.Equal(ErrorMessages.CreateEventInvalid, noType.Error);
        }
    }
}
