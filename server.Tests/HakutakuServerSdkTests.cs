/*!************************************************************************
\file   HakutakuServerSdkTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   4th November 2026
\brief  This file defines the tests for the server SDK (sdk-server/). The
        main one is the error split a game's authenticator relies on: a bad
        ticket is InvalidSessionTicket, a refused server key is
        ServerKeyRejected, and a timeout, no connection or a 5xx are each
        their own code and all count as Hakutaku being unavailable. They
        also check what each call sends and how it reads the answer.

        Tests sdk-server/HakutakuServerInstanceAPI.cs against a real local
        web server (FakeHttpServer), so the timeouts and refused connections
        are real network events. They need no database. Referencing both
        SDKs here also proves they can sit in one project without CS0433.
**************************************************************************/
using System.Net;
using System.Net.Sockets;
using Hakutaku;
using Hakutaku.ServerModels;
using Microsoft.AspNetCore.Http;
using server.Tests.Support;

namespace server.Tests
{
    public class HakutakuServerSdkTests
    {
        const string Key = "test-server-key";

        static HakutakuServerInstanceAPI Sdk(FakeHttpServer server, string? key = Key, TimeSpan? timeout = null) =>
            new(server.Url, key, timeout);

        static Task<HakutakuServerResult<AuthenticateSessionTicketResult>> Validate(HakutakuServerInstanceAPI sdk, string ticket = "a-ticket") =>
            sdk.AuthenticateSessionTicketAsync(new AuthenticateSessionTicketRequest { SessionTicket = ticket });

        static void Answer(FakeHttpServer server, IResult result) => server.GameServerRoutes = _ => Task.FromResult(result);

        // ---- AuthenticateSessionTicket

        [Fact]
        public async Task A_good_ticket_gives_the_player_and_sends_the_key_and_the_ticket()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { playerId = "p-1", displayName = "Sherwyn" }));

            var reply = await Validate(Sdk(server), "the-ticket");

            Assert.Null(reply.Error);
            Assert.Equal("p-1", reply.Result!.PlayerId);
            Assert.Equal("Sherwyn", reply.Result.DisplayName);
            var sent = server.LastGameServerRequest!.Value;
            Assert.Equal(("POST", "/api/server/sessions/validate", Key), (sent.Method, sent.PathAndQuery, sent.ServerKey));
            Assert.Contains("\"ticket\":\"the-ticket\"", sent.Body);
        }

        [Fact]
        public async Task A_401_with_the_ticket_code_is_an_invalid_ticket_and_not_unavailable()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { error = "unknown, revoked or expired session ticket", code = "invalid_ticket" }, statusCode: 401));

            var error = (await Validate(Sdk(server))).Error!;

            Assert.Equal(HakutakuServerErrorCode.InvalidSessionTicket, error.Error);
            Assert.False(error.IsHakutakuUnavailable);
            Assert.Equal(401, error.HttpCode);
        }

        [Fact]
        public async Task A_401_without_the_code_is_this_servers_key_and_never_the_players_ticket()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { error = "unknown, revoked or expired server key" }, statusCode: 401));

            var error = (await Validate(Sdk(server))).Error!;

            Assert.Equal(HakutakuServerErrorCode.ServerKeyRejected, error.Error);
            Assert.False(error.IsHakutakuUnavailable);
        }

        [Fact]
        public async Task A_timeout_is_its_own_error_and_counts_as_unavailable()
        {
            await using var server = await FakeHttpServer.Start();
            server.GameServerRoutes = async c =>
            {
                try { await Task.Delay(5000, c.RequestAborted); } catch (OperationCanceledException) { }
                return Results.Json(new { playerId = "late" });
            };

            var started = DateTime.UtcNow;
            var error = (await Validate(Sdk(server, timeout: TimeSpan.FromMilliseconds(300)))).Error!;

            Assert.Equal(HakutakuServerErrorCode.Timeout, error.Error);
            Assert.True(error.IsHakutakuUnavailable);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4), "it gave up at its own timeout, not the server's delay");
        }

        [Fact]
        public async Task No_server_listening_is_a_connection_error_and_counts_as_unavailable()
        {
            // a port that was free a moment ago and has nothing on it now
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            var sdk = new HakutakuServerInstanceAPI($"http://127.0.0.1:{port}", Key);
            var error = (await sdk.AuthenticateSessionTicketAsync(new AuthenticateSessionTicketRequest { SessionTicket = "t" })).Error!;

            Assert.Equal(HakutakuServerErrorCode.ConnectionError, error.Error);
            Assert.True(error.IsHakutakuUnavailable);
        }

        [Theory]
        [InlineData(500, HakutakuServerErrorCode.InternalServerError)]
        [InlineData(502, HakutakuServerErrorCode.ServiceUnavailable)]
        [InlineData(503, HakutakuServerErrorCode.ServiceUnavailable)]
        [InlineData(504, HakutakuServerErrorCode.ServiceUnavailable)]
        public async Task A_5xx_counts_as_unavailable(int status, HakutakuServerErrorCode expected)
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.StatusCode(status));

            var error = (await Validate(Sdk(server))).Error!;

            Assert.Equal(expected, error.Error);
            Assert.True(error.IsHakutakuUnavailable);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task With_no_key_nothing_is_sent(string? key)
        {
            await using var server = await FakeHttpServer.Start();

            var error = (await Validate(Sdk(server, key))).Error!;

            Assert.Equal(HakutakuServerErrorCode.MissingServerKey, error.Error);
            Assert.False(error.IsHakutakuUnavailable);
            Assert.Equal(0, server.Requests);
        }

        [Fact]
        public async Task The_callback_form_gets_the_same_error()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { error = "bad", code = "invalid_ticket" }, statusCode: 401));
            var done = new TaskCompletionSource<HakutakuServerError>();

            Sdk(server).AuthenticateSessionTicket(new AuthenticateSessionTicketRequest { SessionTicket = "t" },
                _ => done.SetException(new Exception("expected an error")), e => done.SetResult(e));

            Assert.Equal(HakutakuServerErrorCode.InvalidSessionTicket, (await done.Task.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        }

        // ---- GetUserReadOnlyData

        [Fact]
        public async Task Reading_asks_for_the_read_only_tier_and_the_keys()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { data = new Dictionary<string, string> { ["Xp"] = "1200" } }));

            var reply = await Sdk(server).GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayerId = "p-1", Keys = ["Xp", "Gold"] });

            Assert.Null(reply.Error);
            Assert.Equal("1200", reply.Result!.Data["Xp"]);
            Assert.Equal("p-1", reply.Result.PlayerId);
            var sent = server.LastGameServerRequest!.Value;
            Assert.Equal(("GET", "/api/server/players/p-1/data?tier=ReadOnly&keys=Xp%2CGold"), (sent.Method, sent.PathAndQuery));
        }

        [Fact]
        public async Task Reading_with_no_keys_asks_for_every_key_and_never_sends_an_empty_keys()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { data = new Dictionary<string, string>() }));

            await Sdk(server).GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayerId = "p-1" });
            var withNull = server.LastGameServerRequest!.Value.PathAndQuery;
            await Sdk(server).GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayerId = "p-1", Keys = [] });
            var withEmpty = server.LastGameServerRequest!.Value.PathAndQuery;

            Assert.Equal("/api/server/players/p-1/data?tier=ReadOnly", withNull);
            Assert.Equal(withNull, withEmpty);
        }

        [Fact]
        public async Task A_missing_key_is_simply_not_in_the_result()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { data = new Dictionary<string, string>() }));

            var reply = await Sdk(server).GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayerId = "p-1", Keys = ["Xp"] });

            Assert.Null(reply.Error);
            Assert.False(reply.Result!.Data.ContainsKey("Xp"));
        }

        [Fact]
        public async Task The_player_id_is_escaped_in_the_path()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { data = new Dictionary<string, string>() }));

            await Sdk(server).GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayerId = "a/b?c" });

            Assert.StartsWith("/api/server/players/a%2Fb%3Fc/data?tier=ReadOnly", server.LastGameServerRequest!.Value.PathAndQuery);
        }

        [Fact]
        public async Task An_unknown_player_is_not_found()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { error = "no such player" }, statusCode: 404));

            var error = (await Sdk(server).GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayerId = "p-1" })).Error!;

            Assert.Equal(HakutakuServerErrorCode.NotFound, error.Error);
            Assert.Equal("no such player", error.ErrorMessage);
        }

        // ---- UpdateUserReadOnlyData

        [Fact]
        public async Task Writing_sends_the_values_and_the_removals_and_reads_back_the_count()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { updated = 2 }));

            var reply = await Sdk(server).UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest
            {
                PlayerId = "p-1",
                Data = new Dictionary<string, string> { ["Xp"] = "1300" },
                KeysToRemove = ["Old"],
            });

            Assert.Null(reply.Error);
            Assert.Equal(2, reply.Result!.Updated);
            var sent = server.LastGameServerRequest!.Value;
            Assert.Equal(("POST", "/api/server/players/p-1/data?tier=ReadOnly"), (sent.Method, sent.PathAndQuery));
            Assert.Contains("\"data\":{\"Xp\":\"1300\"}", sent.Body);
            Assert.Contains("\"keysToRemove\":[\"Old\"]", sent.Body);
        }

        [Fact]
        public async Task A_refused_write_is_invalid_params_with_the_servers_message()
        {
            await using var server = await FakeHttpServer.Start();
            Answer(server, Results.Json(new { error = "at most 10 keys can be changed in one request" }, statusCode: 400));

            var error = (await Sdk(server).UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest { PlayerId = "p-1" })).Error!;

            Assert.Equal(HakutakuServerErrorCode.InvalidParams, error.Error);
            Assert.Equal("at most 10 keys can be changed in one request", error.ErrorMessage);
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public async Task A_blank_player_id_is_refused_without_sending(string playerId)
        {
            await using var server = await FakeHttpServer.Start();
            var sdk = Sdk(server);

            var read = await sdk.GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayerId = playerId });
            var write = await sdk.UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest { PlayerId = playerId });

            Assert.Equal(HakutakuServerErrorCode.InvalidParams, read.Error!.Error);
            Assert.Equal(HakutakuServerErrorCode.InvalidParams, write.Error!.Error);
            Assert.Equal(0, server.Requests);
        }
    }
}
