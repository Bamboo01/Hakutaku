/*!************************************************************************
\file   HakutakuHttpTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the tests for HakutakuHttp, the HTTP code that the
        client SDK and the server SDK share. A session ticket goes as a
        Bearer token and a server key in the X-Server-Key header. A code
        sent with an error is kept. A timeout is its own error, kept apart
        from a refused connection, and a 5xx is kept apart from both. They
        run against a real local web server, so these are real network
        events.

        Tests sdk/HakutakuHttp.cs and sdk/HakutakuError.cs through
        FakeHttpServer. They need no database.
**************************************************************************/
using Hakutaku;
using server.Tests.Support;

namespace server.Tests
{
    public class HakutakuHttpTests
    {
        // the shape of what the fake server's /headers answers with
        class HeadersWire
        {
            public string? Authorization { get; set; }
            public string? ServerKey { get; set; }
            public string? Method { get; set; }
        }

        class ValueWire
        {
            public string? Value { get; set; }
        }

        static Task<HakutakuResult<string>> Get(string url, string path, string? ticket = null, string? serverKey = null, TimeSpan? timeout = null) =>
            HakutakuHttp.SendRawAsync(url, "GET", path, null, ticket, serverKey, timeout);

        // ---- which headers go out

        [Fact]
        public async Task A_session_ticket_goes_as_a_bearer_token_and_there_is_no_server_key()
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await HakutakuHttp.SendAsync<HeadersWire>(server.Url, "GET", "/headers", null, "my-ticket");

            Assert.Null(reply.Error);
            Assert.Equal("Bearer my-ticket", reply.Result!.Authorization);
            Assert.Equal("", reply.Result.ServerKey);
        }

        [Fact]
        public async Task A_server_key_goes_in_the_X_Server_Key_header_and_there_is_no_bearer()
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await HakutakuHttp.SendAsync<HeadersWire>(server.Url, "GET", "/headers", null, null, "my-key");

            Assert.Null(reply.Error);
            Assert.Equal("my-key", reply.Result!.ServerKey);
            Assert.Equal("", reply.Result.Authorization);
        }

        [Fact]
        public async Task Neither_header_is_sent_when_neither_is_given_and_an_empty_one_counts_as_none()
        {
            await using var server = await FakeHttpServer.Start();

            var none = await HakutakuHttp.SendAsync<HeadersWire>(server.Url, "GET", "/headers", null, null);
            var empty = await HakutakuHttp.SendAsync<HeadersWire>(server.Url, "GET", "/headers", null, "", "");

            foreach (var reply in new[] { none, empty })
            {
                Assert.Equal("", reply.Result!.Authorization);
                Assert.Equal("", reply.Result.ServerKey);
            }
        }

        [Fact]
        public async Task Both_can_be_sent_together_and_a_body_goes_as_json()
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await HakutakuHttp.SendRawAsync(server.Url, "POST", "/echo", "{\"ticket\":\"abc\"}", null, "my-key");

            Assert.Null(reply.Error);
            Assert.Equal("{\"ticket\":\"abc\"}", reply.Result);
        }

        [Fact]
        public async Task A_trailing_slash_on_the_url_is_fine()
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await Get(server.Url + "/", "/ok");

            Assert.Null(reply.Error);
        }

        // ---- a success

        [Fact]
        public async Task A_success_is_parsed()
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await HakutakuHttp.SendAsync<ValueWire>(server.Url, "GET", "/ok", null, null);

            Assert.Null(reply.Error);
            Assert.Equal("hi", reply.Result!.Value);
        }

        [Fact]
        public async Task A_success_that_is_not_json_is_a_parse_error_and_not_a_crash()
        {
            await using var server = await FakeHttpServer.Start();

            var notJson = await HakutakuHttp.SendAsync<ValueWire>(server.Url, "GET", "/not-json", null, null);
            var wrongShape = await HakutakuHttp.SendAsync<ValueWire>(server.Url, "GET", "/wrong-shape", null, null);

            Assert.Equal(HakutakuErrorCode.JsonParseError, notJson.Error!.Error);
            Assert.Equal(HakutakuErrorCode.JsonParseError, wrongShape.Error!.Error);
        }

        // ---- errors the server sends

        [Fact]
        public async Task A_401_keeps_the_code_the_server_sent_with_it()
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await Get(server.Url, "/unauthorized-with-code");

            Assert.Equal(HakutakuErrorCode.NotAuthenticated, reply.Error!.Error);
            Assert.Equal(401, reply.Error.HttpCode);
            Assert.Equal("invalid_ticket", reply.Error.ServerCode);
            Assert.Equal("unknown, revoked or expired session ticket", reply.Error.ErrorMessage);
        }

        [Fact]
        public async Task A_401_without_a_code_has_an_empty_one()
        {
            await using var server = await FakeHttpServer.Start();

            var withMessage = await Get(server.Url, "/unauthorized");
            var emptyBody = await Get(server.Url, "/unauthorized-empty");

            Assert.Equal(HakutakuErrorCode.NotAuthenticated, withMessage.Error!.Error);
            Assert.Equal("", withMessage.Error.ServerCode);
            Assert.Equal("nope", withMessage.Error.ErrorMessage);
            Assert.Equal(HakutakuErrorCode.NotAuthenticated, emptyBody.Error!.Error);
            Assert.Equal("", emptyBody.Error.ServerCode);
        }

        [Theory]
        [InlineData("/bad-request", 400, HakutakuErrorCode.InvalidParams)]
        [InlineData("/unauthorized", 401, HakutakuErrorCode.NotAuthenticated)]
        [InlineData("/forbidden", 403, HakutakuErrorCode.NotAuthorized)]
        [InlineData("/missing", 404, HakutakuErrorCode.NotFound)]
        [InlineData("/boom", 500, HakutakuErrorCode.InternalServerError)]
        [InlineData("/bad-gateway", 502, HakutakuErrorCode.ServiceUnavailable)]
        [InlineData("/unavailable", 503, HakutakuErrorCode.ServiceUnavailable)]
        public async Task Each_status_gets_its_own_error_code(string path, int status, HakutakuErrorCode expected)
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await Get(server.Url, path);

            Assert.Equal(status, reply.Error!.HttpCode);
            Assert.Equal(expected, reply.Error.Error);
        }

        // ---- the network

        [Fact]
        public async Task A_server_that_is_too_slow_is_a_timeout_and_not_a_connection_error()
        {
            await using var server = await FakeHttpServer.Start();
            var started = DateTime.UtcNow;

            var reply = await Get(server.Url, "/slow/5000", timeout: TimeSpan.FromMilliseconds(300));

            Assert.Equal(HakutakuErrorCode.Timeout, reply.Error!.Error);
            // nothing came back, so there is no status
            Assert.Equal(0, reply.Error.HttpCode);
            // and it gave up when asked to, not after the server's five seconds
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task A_server_that_is_not_there_is_a_connection_error_and_not_a_timeout()
        {
            string deadUrl;
            await using (var server = await FakeHttpServer.Start())
                deadUrl = server.Url;

            var reply = await Get(deadUrl, "/ok", timeout: TimeSpan.FromSeconds(5));

            Assert.Equal(HakutakuErrorCode.ConnectionError, reply.Error!.Error);
            Assert.Equal(0, reply.Error.HttpCode);
        }

        [Theory]
        [InlineData("not a url")]
        [InlineData("")]
        [InlineData("http://")]
        public async Task A_bad_url_is_a_connection_error_and_never_a_crash(string url)
        {
            var reply = await Get(url, "/ok");

            Assert.Equal(HakutakuErrorCode.ConnectionError, reply.Error!.Error);
        }

        [Fact]
        public async Task A_slow_answer_is_fine_when_it_arrives_within_the_timeout()
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await Get(server.Url, "/slow/200", timeout: TimeSpan.FromSeconds(5));

            Assert.Null(reply.Error);
        }

        [Fact]
        public async Task A_slow_answer_is_fine_when_no_timeout_is_given()
        {
            await using var server = await FakeHttpServer.Start();

            var reply = await Get(server.Url, "/slow/400");

            Assert.Null(reply.Error);
        }

        [Fact]
        public async Task A_timeout_is_for_that_request_only()
        {
            await using var server = await FakeHttpServer.Start();

            var slow = await Get(server.Url, "/slow/5000", timeout: TimeSpan.FromMilliseconds(200));
            var next = await Get(server.Url, "/ok");

            Assert.Equal(HakutakuErrorCode.Timeout, slow.Error!.Error);
            Assert.Null(next.Error);
        }

        [Fact]
        public async Task Requests_at_the_same_time_each_keep_their_own_timeout_and_headers()
        {
            await using var server = await FakeHttpServer.Start();

            var slow = Get(server.Url, "/slow/5000", timeout: TimeSpan.FromMilliseconds(300));
            var one = HakutakuHttp.SendAsync<HeadersWire>(server.Url, "GET", "/headers", null, "ticket-one");
            var two = HakutakuHttp.SendAsync<HeadersWire>(server.Url, "GET", "/headers", null, null, "key-two");
            await Task.WhenAll(slow, one, two);

            Assert.Equal(HakutakuErrorCode.Timeout, slow.Result.Error!.Error);
            Assert.Equal("Bearer ticket-one", one.Result.Result!.Authorization);
            Assert.Equal("", one.Result.Result.ServerKey);
            Assert.Equal("key-two", two.Result.Result!.ServerKey);
            Assert.Equal("", two.Result.Result.Authorization);
        }

        // ---- the list of error codes

        [Fact]
        public void The_error_codes_that_were_there_before_keep_their_numbers_and_Timeout_comes_last()
        {
            // a game may have stored or compared these, so adding one must not renumber the rest
            Assert.Equal(0, (int)HakutakuErrorCode.Unknown);
            Assert.Equal(1, (int)HakutakuErrorCode.NotLoggedIn);
            Assert.Equal(2, (int)HakutakuErrorCode.ConnectionError);
            Assert.Equal(3, (int)HakutakuErrorCode.JsonParseError);
            Assert.Equal(4, (int)HakutakuErrorCode.InvalidParams);
            Assert.Equal(5, (int)HakutakuErrorCode.NotAuthenticated);
            Assert.Equal(6, (int)HakutakuErrorCode.NotAuthorized);
            Assert.Equal(7, (int)HakutakuErrorCode.NotFound);
            Assert.Equal(8, (int)HakutakuErrorCode.Conflict);
            Assert.Equal(9, (int)HakutakuErrorCode.TooManyRequests);
            Assert.Equal(10, (int)HakutakuErrorCode.InternalServerError);
            Assert.Equal(11, (int)HakutakuErrorCode.ServiceUnavailable);
            Assert.Equal(12, (int)HakutakuErrorCode.Timeout);
            Assert.Equal(13, Enum.GetValues<HakutakuErrorCode>().Length);
        }

        [Fact]
        public void The_error_report_names_the_route_and_the_problem()
        {
            var error = new HakutakuError { ApiEndpoint = "/x", HttpCode = 401, HttpStatus = "Unauthorized", ErrorMessage = "nope" };
            var noResponse = new HakutakuError { ApiEndpoint = "/x", Error = HakutakuErrorCode.Timeout, ErrorMessage = "slow" };

            Assert.Equal("/x: nope (HTTP 401 Unauthorized)", error.GenerateErrorReport());
            Assert.Equal("/x: slow (Timeout)", noResponse.GenerateErrorReport());
        }
    }
}
