/*!************************************************************************
\file   SessionValidateTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the integration tests for POST
        /api/server/sessions/validate. A game server sends a player's
        session ticket and gets the player back. An unknown, revoked or
        expired ticket is a 401 with the invalid_ticket code, and so is an
        empty or oversized one, never a 400. A bad server key is also a 401
        but has no such code, so a game can tell the two apart. Validating
        writes nothing.

        Runs against the real server through HakutakuFactory. Tests
        ValidateSession.cs and PlayerSessionService.FindByToken.
**************************************************************************/
using System.Net;
using server.Constants;
using server.Endpoints.GameServer;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class SessionValidateTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>, IAsyncLifetime
    {
        const string Validate = "/api/server/sessions/validate";

        readonly ApiClient api = app.Api();
        string key = "";

        // a server key for each test, made by the owner
        public async Task InitializeAsync()
        {
            var owner = app.Api();
            await owner.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            var made = await owner.Post("/api/admin/server-keys", new { name = "validate-" + Guid.NewGuid().ToString("N")[..8] });
            Assert.Equal(HttpStatusCode.OK, made.Status);
            key = made.Json.GetProperty("key").GetString()!;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        Task<Reply> Send(object body, string? serverKey = null) => api.Post(Validate, body, serverKey: serverKey ?? key);

        Task<Reply> Check(string? ticket, string? serverKey = null) => Send(new { ticket }, serverKey);

        // a 401 that says the ticket is what's bad
        static void AssertBadTicket(Reply reply)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
            Assert.Equal(ErrorMessages.InvalidSessionTicket, reply.Error);
            Assert.Equal(ValidateSession.InvalidTicketCode, reply.Json.GetProperty("code").GetString());
        }

        // a 401 that is not about the ticket: no code, so a game can see it's the server key that was refused
        static void AssertNotATicketError(Reply reply)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
            Assert.False(reply.Json.TryGetProperty("code", out _));
        }

        // ---- a good ticket

        [Fact]
        public async Task A_real_ticket_gets_the_player_back()
        {
            var guest = await api.RegisterGuest();

            var reply = await Check(guest.Token);

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(guest.Id, reply.Json.GetProperty("playerId").GetGuid());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, reply.Json.GetProperty("displayName").ValueKind);
        }

        [Fact]
        public async Task The_display_name_comes_back_when_the_player_has_one()
        {
            var guest = await api.RegisterGuest();
            await api.SetDisplayName(guest.Token, "Sir Bob");

            var reply = await Check(guest.Token);

            Assert.Equal("Sir Bob", reply.Json.GetProperty("displayName").GetString());
        }

        [Fact]
        public async Task Every_session_a_player_holds_is_valid_on_its_own()
        {
            var guest = await api.RegisterGuest();
            var second = await api.Post("/api/players/login/device", new { deviceToken = guest.DeviceToken });
            var secondTicket = second.Json.GetProperty("token").GetString()!;

            Assert.NotEqual(guest.Token, secondTicket);
            Assert.Equal(guest.Id, (await Check(guest.Token)).Json.GetProperty("playerId").GetGuid());
            Assert.Equal(guest.Id, (await Check(secondTicket)).Json.GetProperty("playerId").GetGuid());
        }

        [Fact]
        public async Task Two_players_get_their_own_ids()
        {
            var one = await api.RegisterGuest();
            var two = await api.RegisterGuest();

            Assert.Equal(one.Id, (await Check(one.Token)).Json.GetProperty("playerId").GetGuid());
            Assert.Equal(two.Id, (await Check(two.Token)).Json.GetProperty("playerId").GetGuid());
        }

        [Fact]
        public async Task A_ticket_with_spaces_around_it_still_works()
        {
            var guest = await api.RegisterGuest();

            var reply = await Check($"  {guest.Token}  ");

            Assert.Equal(HttpStatusCode.OK, reply.Status);
        }

        // ---- a bad ticket

        [Fact]
        public async Task An_unknown_ticket_is_401_with_the_ticket_code()
        {
            AssertBadTicket(await Check("not-a-real-ticket"));
            AssertBadTicket(await Check(new string('a', 43)));
        }

        [Fact]
        public async Task Tickets_that_are_not_the_real_one_are_all_bad()
        {
            var guest = await api.RegisterGuest();

            // the device token is a credential too, but not a session ticket
            AssertBadTicket(await Check(guest.DeviceToken));
            // a server key is not a ticket either
            AssertBadTicket(await Check(key));
            // close but wrong: cut short, changed case, one character different
            AssertBadTicket(await Check(guest.Token[..^1]));
            AssertBadTicket(await Check(guest.Token.ToUpperInvariant()));
            AssertBadTicket(await Check(guest.Token[..^1] + (guest.Token[^1] == 'a' ? 'b' : 'a')));
        }

        [Fact]
        public async Task An_empty_or_missing_ticket_is_a_bad_ticket_and_never_a_400()
        {
            AssertBadTicket(await Check(""));
            AssertBadTicket(await Check("   "));
            AssertBadTicket(await Check(null));
            AssertBadTicket(await Send(new { }));
            AssertBadTicket(await Send(new { somethingElse = "x" }));
        }

        [Fact]
        public async Task A_ticket_that_is_far_too_long_is_a_bad_ticket_and_never_a_400()
        {
            AssertBadTicket(await Check(new string('a', Limits.SessionTicketMaxLength + 1)));
            AssertBadTicket(await Check(new string('a', 200_000)));
        }

        [Theory]
        [InlineData("a b c")]
        [InlineData("../../etc/passwd")]
        [InlineData("'; drop table player_sessions; --")]
        [InlineData("名前")]
        [InlineData("a\nb")]
        [InlineData("\0")]
        public async Task Odd_text_is_a_bad_ticket_and_not_a_server_error(string ticket)
        {
            AssertBadTicket(await Check(ticket));
        }

        [Fact]
        public async Task A_revoked_ticket_is_bad()
        {
            var guest = await api.RegisterGuest();
            Assert.Equal(HttpStatusCode.OK, (await Check(guest.Token)).Status);

            await app.Run($"update player_sessions set revoked_at = now() where player_id = '{guest.Id}'");

            AssertBadTicket(await Check(guest.Token));
        }

        [Fact]
        public async Task An_expired_ticket_is_bad_and_one_with_time_left_is_not()
        {
            var guest = await api.RegisterGuest();

            await app.Run($"update player_sessions set expires_at = now() + interval '1 minute' where player_id = '{guest.Id}'");
            Assert.Equal(HttpStatusCode.OK, (await Check(guest.Token)).Status);

            await app.Run($"update player_sessions set expires_at = now() - interval '1 minute' where player_id = '{guest.Id}'");
            AssertBadTicket(await Check(guest.Token));
        }

        [Fact]
        public async Task Revoking_one_session_leaves_the_players_other_sessions_alone()
        {
            var guest = await api.RegisterGuest();
            var second = await api.Post("/api/players/login/device", new { deviceToken = guest.DeviceToken });
            var secondTicket = second.Json.GetProperty("token").GetString()!;

            await app.Run($"update player_sessions set revoked_at = now() where id = (select min(id) from player_sessions where player_id = '{guest.Id}')");

            AssertBadTicket(await Check(guest.Token));
            Assert.Equal(HttpStatusCode.OK, (await Check(secondTicket)).Status);
        }

        // ---- nothing is written

        [Fact]
        public async Task Validating_writes_nothing_to_the_sessions_or_the_player()
        {
            var guest = await api.RegisterGuest();
            await api.SetDisplayName(guest.Token, "Sir Bob");
            async Task<string?> Snapshot() => await app.Scalar<string>(
                $@"select (select string_agg(id || ':' || issued_at || ':' || expires_at || ':' || coalesce(revoked_at::text, ''), ',' order by id) from player_sessions where player_id = '{guest.Id}')
                          || '|' || (select ""DisplayName"" from ""Players"" where ""Id"" = '{guest.Id}')
                          || '|' || (select count(*) from player_sessions)
                          || '|' || (select count(*) from player_data where player_id = '{guest.Id}')");
            var before = await Snapshot();

            await Check(guest.Token);
            await Check(guest.Token);
            await Check("not-a-real-ticket");
            await Check("");

            Assert.NotNull(before);
            Assert.Equal(before, await Snapshot());
        }

        // ---- the server key

        [Fact]
        public async Task Without_a_server_key_it_is_401_and_not_a_ticket_error()
        {
            var guest = await api.RegisterGuest();

            // a perfectly good ticket, but nothing to say who is asking
            var reply = await api.Post(Validate, new { ticket = guest.Token });

            AssertNotATicketError(reply);
        }

        [Fact]
        public async Task A_wrong_server_key_is_401_and_not_a_ticket_error()
        {
            var guest = await api.RegisterGuest();

            AssertNotATicketError(await Check(guest.Token, "not-a-real-key"));
        }

        [Fact]
        public async Task A_revoked_server_key_is_401_and_not_a_ticket_error()
        {
            var guest = await api.RegisterGuest();
            var owner = app.Api();
            await owner.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            var made = await owner.Post("/api/admin/server-keys", new { name = "to-revoke" });
            var revokedKey = made.Json.GetProperty("key").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await Check(guest.Token, revokedKey)).Status);

            await owner.Delete($"/api/admin/server-keys/{made.Json.GetProperty("id").GetInt64()}");

            AssertNotATicketError(await Check(guest.Token, revokedKey));
        }

        [Fact]
        public async Task A_player_ticket_is_not_a_server_key()
        {
            var guest = await api.RegisterGuest();

            AssertNotATicketError(await Check(guest.Token, guest.Token));
            AssertNotATicketError(await api.Post(Validate, new { ticket = guest.Token }, token: guest.Token));
        }
    }
}
