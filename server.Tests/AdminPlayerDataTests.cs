/*!************************************************************************
\file   AdminPlayerDataTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines the integration tests for the admin's player
        routes: GET /api/players/{id}, GET /api/players/{id}/data and POST
        /api/players/{id}/data. An admin sees one player and every tier of
        their data, edits any tier under the same rules as a game server,
        sees what a game server wrote and the other way round, every edit
        is logged with the admin's username and never a value, an unknown
        player is a 404, and nothing but an admin session gets in.

        Runs against the real server through HakutakuFactory. Tests
        GetPlayer.cs, ListPlayerData.cs and EditPlayerData.cs.
**************************************************************************/
using System.Net;
using System.Text.Json;
using server.Constants;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class AdminPlayerDataTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>, IAsyncLifetime
    {
        // signed in as the owner, so its cookie rides along on every call
        readonly ApiClient admin = app.Api();
        // never signed in
        readonly ApiClient anon = app.Api();

        public Task InitializeAsync() => admin.LoginAdmin("admin", HakutakuFactory.OwnerPassword);

        public Task DisposeAsync() => Task.CompletedTask;

        static string DataPath(Guid player) => $"/api/players/{player}/data";

        static Dictionary<string, string?> Set(params (string Key, string? Value)[] pairs) =>
            pairs.ToDictionary(p => p.Key, p => p.Value);

        Task<Reply> Edit(Guid player, string tier, object body) => admin.Post($"{DataPath(player)}?tier={tier}", body);

        // tier name -> key -> value, from the GET answer
        static Dictionary<string, Dictionary<string, string>> Tiers(Reply reply) =>
            reply.Json.GetProperty("tiers").EnumerateArray().ToDictionary(
                t => t.GetProperty("tier").GetString()!,
                t => t.GetProperty("entries").EnumerateArray().ToDictionary(
                    e => e.GetProperty("key").GetString()!, e => e.GetProperty("value").GetString()!));

        async Task<string> ServerKey()
        {
            var made = await admin.Post("/api/admin/server-keys", new { name = "admin-data-" + Guid.NewGuid().ToString("N")[..8] });
            Assert.Equal(HttpStatusCode.OK, made.Status);
            return made.Json.GetProperty("key").GetString()!;
        }

        // ---- one player

        [Fact]
        public async Task An_admin_gets_one_player_with_their_providers()
        {
            var guest = await anon.RegisterGuest();
            await anon.SetDisplayName(guest.Token, "Admin View");

            var reply = await admin.Get($"/api/players/{guest.Id}");

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(guest.Id, reply.Json.GetProperty("id").GetGuid());
            Assert.Equal("Admin View", reply.Json.GetProperty("displayName").GetString());
            Assert.Equal(["device"], reply.Json.GetProperty("providers").EnumerateArray().Select(p => p.GetString()));
        }

        [Fact]
        public async Task One_player_leaves_out_the_identities()
        {
            var guest = await anon.RegisterGuest();

            var body = (await admin.Get($"/api/players/{guest.Id}")).Body;

            Assert.DoesNotContain(guest.DeviceToken, body);
            Assert.DoesNotContain("subject", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hardware", body, StringComparison.OrdinalIgnoreCase);
        }

        // ---- reading every tier

        [Fact]
        public async Task A_player_with_no_data_still_gets_all_three_tiers_in_order()
        {
            var guest = await anon.RegisterGuest();

            var reply = await admin.Get(DataPath(guest.Id));

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            var tiers = reply.Json.GetProperty("tiers").EnumerateArray().ToList();
            Assert.Equal(["ClientWritable", "ReadOnly", "Internal"], tiers.Select(t => t.GetProperty("tier").GetString()));
            Assert.All(tiers, t => Assert.Equal(0, t.GetProperty("entries").GetArrayLength()));
        }

        [Fact]
        public async Task Every_tier_comes_back_sorted_by_key_with_when_it_was_set()
        {
            var guest = await anon.RegisterGuest();
            await Edit(guest.Id, "ReadOnly", new { data = Set(("Xp", "1200"), ("Level", "7")) });
            await Edit(guest.Id, "Internal", new { data = Set(("Flag", "on")) });

            var reply = await admin.Get(DataPath(guest.Id));

            var tiers = Tiers(reply);
            Assert.Empty(tiers["ClientWritable"]);
            Assert.Equal(new Dictionary<string, string> { ["Level"] = "7", ["Xp"] = "1200" }, tiers["ReadOnly"]);
            Assert.Equal(["Level", "Xp"], tiers["ReadOnly"].Keys);
            Assert.Equal(new Dictionary<string, string> { ["Flag"] = "on" }, tiers["Internal"]);
            var entry = reply.Json.GetProperty("tiers")[1].GetProperty("entries")[0];
            Assert.True(entry.GetProperty("updatedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        // ---- editing, and the game server seeing it

        [Fact]
        public async Task An_admin_edit_is_what_the_game_server_reads_and_the_other_way_round()
        {
            var guest = await anon.RegisterGuest();
            var key = await ServerKey();

            var edit = await Edit(guest.Id, "ReadOnly", new { data = Set(("Xp", "500")) });
            var serverRead = await anon.Get($"/api/server/players/{guest.Id}/data?tier=ReadOnly", serverKey: key);
            await anon.Post($"/api/server/players/{guest.Id}/data?tier=ReadOnly", new { data = Set(("Xp", "650")) }, serverKey: key);
            var adminRead = await admin.Get(DataPath(guest.Id));

            Assert.Equal(HttpStatusCode.OK, edit.Status);
            Assert.Equal(1, edit.Json.GetProperty("updated").GetInt32());
            Assert.Equal("500", serverRead.Json.GetProperty("data").GetProperty("Xp").GetString());
            Assert.Equal("650", Tiers(adminRead)["ReadOnly"]["Xp"]);
        }

        [Theory]
        [InlineData("ClientWritable")]
        [InlineData("ReadOnly")]
        [InlineData("Internal")]
        [InlineData("internal")]
        public async Task An_admin_can_edit_any_tier(string tier)
        {
            var guest = await anon.RegisterGuest();

            var edit = await Edit(guest.Id, tier, new { data = Set(("Note", "hi")) });

            Assert.Equal(HttpStatusCode.OK, edit.Status);
            Assert.Equal(1, await app.Scalar<long>($"select count(*) from player_data where player_id = '{guest.Id}'"));
        }

        [Fact]
        public async Task An_edit_can_set_and_remove_in_one_go()
        {
            var guest = await anon.RegisterGuest();
            await Edit(guest.Id, "ReadOnly", new { data = Set(("Old", "1"), ("Keep", "2")) });

            var edit = await Edit(guest.Id, "ReadOnly", new { data = Set(("New", "3")), keysToRemove = new[] { "Old" } });

            Assert.Equal(2, edit.Json.GetProperty("updated").GetInt32());
            Assert.Equal(new Dictionary<string, string> { ["Keep"] = "2", ["New"] = "3" }, Tiers(await admin.Get(DataPath(guest.Id)))["ReadOnly"]);
        }

        // ---- the same rules as the game server

        [Fact]
        public async Task An_edit_is_held_to_the_game_servers_rules_and_writes_nothing_when_refused()
        {
            var guest = await anon.RegisterGuest();
            var eleven = Enumerable.Range(1, Limits.PlayerDataMaxChangesPerRequest + 1).Select(i => ($"K{i}", (string?)"v")).ToArray();

            var badKey = await Edit(guest.Id, "ReadOnly", new { data = Set(("Good", "v"), ("bad key", "v")) });
            var bigValue = await Edit(guest.Id, "ReadOnly", new { data = Set(("Big", new string('v', Limits.PlayerDataValueMaxBytes + 1))) });
            var nullValue = await Edit(guest.Id, "ReadOnly", new { data = Set(("Xp", null)) });
            var twice = await Edit(guest.Id, "ReadOnly", new { data = Set(("Xp", "1")), keysToRemove = new[] { "Xp" } });
            var tooMany = await Edit(guest.Id, "ReadOnly", new { data = Set(eleven) });

            Assert.Equal(ErrorMessages.PlayerDataKeyInvalid, badKey.Error);
            Assert.Equal(ErrorMessages.PlayerDataValueInvalid, bigValue.Error);
            Assert.Equal(ErrorMessages.PlayerDataValueInvalid, nullValue.Error);
            Assert.Equal(ErrorMessages.PlayerDataKeyTwice, twice.Error);
            Assert.Equal(ErrorMessages.PlayerDataTooManyChanges, tooMany.Error);
            Assert.All([badKey, bigValue, nullValue, twice, tooMany], r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));
            Assert.Equal(0, await app.Scalar<long>($"select count(*) from player_data where player_id = '{guest.Id}'"));
        }

        [Fact]
        public async Task An_edit_past_100_keys_in_a_tier_is_refused()
        {
            var guest = await anon.RegisterGuest();
            for (var start = 1; start <= Limits.PlayerDataMaxKeysPerTier; start += Limits.PlayerDataMaxChangesPerRequest)
            {
                var batch = Enumerable.Range(start, Limits.PlayerDataMaxChangesPerRequest).Select(i => ($"N{i}", (string?)"v")).ToArray();
                Assert.Equal(HttpStatusCode.OK, (await Edit(guest.Id, "Internal", new { data = Set(batch) })).Status);
            }

            var extra = await Edit(guest.Id, "Internal", new { data = Set(("OneTooMany", "v")) });

            Assert.Equal(HttpStatusCode.BadRequest, extra.Status);
            Assert.Equal(ErrorMessages.PlayerDataTooManyKeys, extra.Error);
        }

        [Theory]
        [InlineData("")]
        [InlineData("?tier=")]
        [InlineData("?tier=1")]
        [InlineData("?tier=Public")]
        public async Task An_edit_without_a_known_tier_is_refused(string query)
        {
            var guest = await anon.RegisterGuest();

            var reply = await admin.Post(DataPath(guest.Id) + query, new { data = Set(("Xp", "1")) });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(ErrorMessages.PlayerDataTierInvalid, reply.Error);
        }

        // ---- the log

        [Fact]
        public async Task An_edit_is_logged_with_the_admin_and_keys_but_never_a_value()
        {
            var guest = await anon.RegisterGuest();
            const string secret = "value-that-must-not-be-logged";

            await Edit(guest.Id, "ReadOnly", new { data = Set(("Xp", secret)) });
            await Edit(guest.Id, "ReadOnly", new { keysToRemove = new[] { "Xp" } });

            var lines = app.Logs.In(LogCategories.AdminActions).Where(l => l.Message.Contains(guest.Id.ToString())).ToList();
            Assert.Equal(2, lines.Count);
            Assert.All(lines, l => Assert.Contains("Admin admin", l.Message));
            Assert.Contains("set [Xp]", lines[0].Message);
            Assert.Contains("removed [Xp]", lines[1].Message);
            Assert.DoesNotContain(app.Logs.In(LogCategories.AdminActions), l => l.Message.Contains(secret));
        }

        [Fact]
        public async Task A_refused_edit_is_not_logged()
        {
            var guest = await anon.RegisterGuest();

            await Edit(guest.Id, "ReadOnly", new { data = Set(("bad key", "v")) });

            Assert.DoesNotContain(app.Logs.In(LogCategories.AdminActions), l => l.Message.Contains(guest.Id.ToString()));
        }

        // ---- which player, and who gets in

        [Fact]
        public async Task An_unknown_player_is_404_on_all_three_routes()
        {
            var nobody = Guid.NewGuid();

            var one = await admin.Get($"/api/players/{nobody}");
            var read = await admin.Get(DataPath(nobody));
            var edit = await Edit(nobody, "ReadOnly", new { data = Set(("Xp", "1")) });

            Assert.All([one, read, edit], r =>
            {
                Assert.Equal(HttpStatusCode.NotFound, r.Status);
                Assert.Equal(ErrorMessages.NoSuchPlayer, r.Error);
            });
            Assert.Equal(0, await app.Scalar<long>($"select count(*) from player_data where player_id = '{nobody}'"));
        }

        [Fact]
        public async Task Not_signed_in_all_three_routes_are_401_and_nothing_is_written()
        {
            var guest = await anon.RegisterGuest();

            var one = await anon.Get($"/api/players/{guest.Id}");
            var read = await anon.Get(DataPath(guest.Id));
            var edit = await anon.Post($"{DataPath(guest.Id)}?tier=ReadOnly", new { data = Set(("Xp", "999999")) });

            Assert.All([one, read, edit], r => Assert.Equal(HttpStatusCode.Unauthorized, r.Status));
            Assert.Equal(0, await app.Scalar<long>($"select count(*) from player_data where player_id = '{guest.Id}'"));
        }

        [Fact]
        public async Task A_player_token_or_a_server_key_does_not_open_the_admin_routes()
        {
            var guest = await anon.RegisterGuest();
            var key = await ServerKey();

            var withToken = await anon.Post($"{DataPath(guest.Id)}?tier=ReadOnly", new { data = Set(("Xp", "999999")) }, token: guest.Token);
            var withKey = await anon.Post($"{DataPath(guest.Id)}?tier=ReadOnly", new { data = Set(("Xp", "999999")) }, serverKey: key);
            var readWithToken = await anon.Get(DataPath(guest.Id), token: guest.Token);

            Assert.All([withToken, withKey, readWithToken], r => Assert.Equal(HttpStatusCode.Unauthorized, r.Status));
            Assert.Equal(0, await app.Scalar<long>($"select count(*) from player_data where player_id = '{guest.Id}'"));
        }

        [Fact]
        public async Task The_public_sign_in_paths_still_reach_their_own_routes()
        {
            // the admin routes take /{id:guid}, so a word like "register" must never land on them
            var reply = await anon.Post("/api/players/register", new { hardwareId = "test-" + Guid.NewGuid().ToString("N") });

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(JsonValueKind.String, reply.Json.GetProperty("deviceToken").ValueKind);
        }
    }
}
