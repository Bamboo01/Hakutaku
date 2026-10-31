/*!************************************************************************
\file   ServerKeyTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the integration tests for server keys. The owner
        can make a key, which is shown once while only its hash is stored,
        and list and revoke keys. On the server routes a missing, wrong,
        revoked or expired key gets a 401 and a fresh key works. No other
        admin can manage keys, and a server key never works on the admin
        routes.

        Runs against the real server through HakutakuFactory. Tests the
        server key routes in Endpoints/Admin, RequireServerKeyFilter and
        /api/server/ping.
**************************************************************************/
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class ServerKeyTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>
    {
        const string Ping = "/api/server/ping";

        // the owner, signed in. each person gets their own client, because the session cookie lives on it
        async Task<ApiClient> Owner()
        {
            var owner = app.Api();
            await owner.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            return owner;
        }

        static string NewName() => "key-" + Guid.NewGuid().ToString("N")[..8];

        // makes a key as the owner and returns its id and the key itself
        static async Task<(long Id, string Key)> MakeKey(ApiClient owner, object? body = null)
        {
            var reply = await owner.Post("/api/admin/server-keys", body ?? new { name = NewName() });
            Assert.Equal(HttpStatusCode.OK, reply.Status);
            return (reply.Json.GetProperty("id").GetInt64(), reply.Json.GetProperty("key").GetString()!);
        }

        // ---- making, listing and revoking

        [Fact]
        public async Task A_new_key_is_shown_once_and_only_its_hash_is_stored()
        {
            var owner = await Owner();
            var name = NewName();

            var reply = await owner.Post("/api/admin/server-keys", new { name });

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            var key = reply.Json.GetProperty("key").GetString()!;
            Assert.Equal(name, reply.Json.GetProperty("name").GetString());
            // 32 random bytes as url-safe base64 is 43 characters, with none of + / =
            Assert.Equal(43, key.Length);
            Assert.Matches("^[A-Za-z0-9_-]+$", key);

            // the table holds the 32-byte hash, which is not the key, and the list never shows the key
            var id = reply.Json.GetProperty("id").GetInt64();
            var stored = await app.Scalar<string>($"select encode(key_sha256, 'hex') from server_keys where id = {id}");
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant(), stored);
            var list = await owner.Get("/api/admin/server-keys");
            Assert.DoesNotContain(key, list.Body);
            Assert.DoesNotContain("key", list.Json.EnumerateArray().First(k => k.GetProperty("id").GetInt64() == id).EnumerateObject().Select(p => p.Name));
        }

        [Fact]
        public async Task Two_keys_are_never_the_same()
        {
            var owner = await Owner();
            var (_, a) = await MakeKey(owner);
            var (_, b) = await MakeKey(owner);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public async Task The_list_shows_name_and_dates_and_never_the_key()
        {
            var owner = await Owner();
            var name = NewName();
            var expires = DateTimeOffset.UtcNow.AddDays(30);
            var (id, key) = await MakeKey(owner, new { name, expiresAt = expires });

            var list = await owner.Get("/api/admin/server-keys");

            Assert.Equal(HttpStatusCode.OK, list.Status);
            var row = list.Json.EnumerateArray().First(k => k.GetProperty("id").GetInt64() == id);
            Assert.Equal(name, row.GetProperty("name").GetString());
            Assert.NotEqual(JsonValueKind.Null, row.GetProperty("createdAt").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, row.GetProperty("expiresAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("revokedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("lastUsedAt").ValueKind);
            Assert.DoesNotContain(key, list.Body);
        }

        [Fact]
        public async Task Making_a_key_checks_the_name_and_the_expiry()
        {
            var owner = await Owner();
            var past = DateTimeOffset.UtcNow.AddMinutes(-5);

            foreach (var bad in new object[]
            {
                new { },
                new { name = "" },
                new { name = "   " },
                new { name = new string('x', 101) },
                new { name = "bad\nname" },
                new { name = "ok", expiresAt = past },
            })
                Assert.Equal(HttpStatusCode.BadRequest, (await owner.Post("/api/admin/server-keys", bad)).Status);

            Assert.Equal(HttpStatusCode.OK, (await owner.Post("/api/admin/server-keys", new { name = new string('x', 100) })).Status);
        }

        [Fact]
        public async Task Revoking_is_one_way_and_keeps_the_first_time()
        {
            var owner = await Owner();
            var (id, _) = await MakeKey(owner);

            Assert.Equal(HttpStatusCode.NoContent, (await owner.Delete($"/api/admin/server-keys/{id}")).Status);
            var first = await app.Scalar<string>($"select revoked_at::text from server_keys where id = {id}");
            await Task.Delay(50);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.Delete($"/api/admin/server-keys/{id}")).Status);

            Assert.NotNull(first);
            Assert.Equal(first, await app.Scalar<string>($"select revoked_at::text from server_keys where id = {id}"));
            Assert.Equal(HttpStatusCode.NotFound, (await owner.Delete("/api/admin/server-keys/999999")).Status);
        }

        // ---- the server routes

        [Fact]
        public async Task A_fresh_key_works_and_the_reply_says_which_key_it_was()
        {
            var owner = await Owner();
            var (id, key) = await MakeKey(owner);

            var reply = await app.Api().Get(Ping, serverKey: key);

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(id, reply.Json.GetProperty("keyId").GetInt64());
        }

        [Fact]
        public async Task No_key_and_a_wrong_key_both_get_401()
        {
            var api = app.Api();

            var none = await api.Get(Ping);
            var wrong = await api.Get(Ping, serverKey: "not-a-real-key");
            var blank = await api.Get(Ping, serverKey: "   ");

            Assert.Equal(HttpStatusCode.Unauthorized, none.Status);
            Assert.Equal(Auth.RequireServerKeyFilter.MissingKeyError, none.Error);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
            Assert.Equal(Auth.RequireServerKeyFilter.InvalidKeyError, wrong.Error);
            Assert.Equal(HttpStatusCode.Unauthorized, blank.Status);
        }

        [Fact]
        public async Task A_revoked_key_stops_working_at_once()
        {
            var owner = await Owner();
            var (id, key) = await MakeKey(owner);
            var api = app.Api();
            Assert.Equal(HttpStatusCode.OK, (await api.Get(Ping, serverKey: key)).Status);

            await owner.Delete($"/api/admin/server-keys/{id}");

            var reply = await api.Get(Ping, serverKey: key);
            Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
            Assert.Equal(Auth.RequireServerKeyFilter.InvalidKeyError, reply.Error);
        }

        [Fact]
        public async Task An_expired_key_gets_401_and_a_key_with_time_left_works()
        {
            var owner = await Owner();
            var (id, key) = await MakeKey(owner, new { name = NewName(), expiresAt = DateTimeOffset.UtcNow.AddDays(1) });
            var api = app.Api();
            Assert.Equal(HttpStatusCode.OK, (await api.Get(Ping, serverKey: key)).Status);

            // a key can't be made already expired, so let the day run out in the table instead
            await app.Run($"update server_keys set expires_at = now() - interval '1 minute' where id = {id}");

            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get(Ping, serverKey: key)).Status);
        }

        [Fact]
        public async Task With_no_keys_at_all_every_server_route_is_refused()
        {
            await app.Run("delete from server_keys");

            var reply = await app.Api().Get(Ping, serverKey: "anything");

            Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        }

        [Fact]
        public async Task Last_used_is_written_at_most_once_a_minute()
        {
            var owner = await Owner();
            var (id, key) = await MakeKey(owner);
            var api = app.Api();
            Assert.Null(await app.Scalar<string>($"select last_used_at::text from server_keys where id = {id}"));

            await api.Get(Ping, serverKey: key);
            var first = await app.Scalar<string>($"select last_used_at::text from server_keys where id = {id}");
            Assert.NotNull(first);

            await Task.Delay(50);
            await api.Get(Ping, serverKey: key);
            Assert.Equal(first, await app.Scalar<string>($"select last_used_at::text from server_keys where id = {id}"));

            // after a minute has passed it is written again
            await app.Run($"update server_keys set last_used_at = now() - interval '2 minutes' where id = {id}");
            await api.Get(Ping, serverKey: key);
            Assert.True(await app.Scalar<bool>($"select last_used_at > now() - interval '1 minute' from server_keys where id = {id}"));
        }

        // ---- who may manage keys, and what a key can't do

        [Fact]
        public async Task An_admin_who_is_not_the_owner_cannot_make_list_or_revoke_keys()
        {
            var owner = await Owner();
            var (id, _) = await MakeKey(owner);
            var helper = "helper" + Guid.NewGuid().ToString("N")[..8];
            Assert.Equal(HttpStatusCode.OK, (await owner.Post("/api/admin/admins", new { username = helper, password = ApiClient.Password })).Status);
            var admin = app.Api();
            await admin.LoginAdmin(helper, ApiClient.Password);

            Assert.Equal(HttpStatusCode.Forbidden, (await admin.Post("/api/admin/server-keys", new { name = "nope" })).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.Get("/api/admin/server-keys")).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.Delete($"/api/admin/server-keys/{id}")).Status);
            // and the key they tried to revoke still works
            Assert.Null(await app.Scalar<string>($"select revoked_at::text from server_keys where id = {id}"));
        }

        [Fact]
        public async Task Signed_out_calls_to_the_key_routes_are_401()
        {
            var api = app.Api();

            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Post("/api/admin/server-keys", new { name = "nope" })).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/admin/server-keys")).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Delete("/api/admin/server-keys/1")).Status);
        }

        [Fact]
        public async Task A_server_key_never_works_on_the_admin_routes()
        {
            var owner = await Owner();
            var (_, key) = await MakeKey(owner);
            var api = app.Api();

            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/admin/server-keys", serverKey: key)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/admin/admins", serverKey: key)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get("/api/admin/me", serverKey: key)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Post("/api/admin/server-keys", new { name = "nope" }, serverKey: key)).Status);
        }

        [Fact]
        public async Task A_player_token_is_not_a_server_key()
        {
            var api = app.Api();
            var guest = await api.RegisterGuest();

            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get(Ping, token: guest.Token)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get(Ping, serverKey: guest.Token)).Status);
        }
    }
}
