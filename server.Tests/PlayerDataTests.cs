/*!************************************************************************
\file   PlayerDataTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the integration tests for the player data routes,
        GET and POST /api/server/players/{id}/data. A game server writes a
        value and reads it back, a write sets and never adds, missing keys
        are left out, tiers are kept apart, every limit is refused without
        writing anything, the tier is required, an unknown player is a 404,
        and only a server key gets in. Writes that arrive at the same time
        cannot push a player past the key limit.

        Runs against the real server through HakutakuFactory. Tests
        GetPlayerData.cs, UpdatePlayerData.cs and PlayerDataService.cs.
**************************************************************************/
using System.Net;
using server.Constants;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class PlayerDataTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>, IAsyncLifetime
    {
        readonly ApiClient api = app.Api();
        string key = "";

        // a server key for each test, made by the owner
        public async Task InitializeAsync()
        {
            var owner = app.Api();
            await owner.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            var made = await owner.Post("/api/admin/server-keys", new { name = "player-data-" + Guid.NewGuid().ToString("N")[..8] });
            Assert.Equal(HttpStatusCode.OK, made.Status);
            key = made.Json.GetProperty("key").GetString()!;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        static string Path(Guid player) => $"/api/server/players/{player}/data";

        Task<Reply> Write(Guid player, string? tier, object body) =>
            api.Post(tier is null ? Path(player) : $"{Path(player)}?tier={tier}", body, serverKey: key);

        Task<Reply> Read(Guid player, string? tier, string? keys = null)
        {
            var query = new List<string>();
            if (tier is not null) query.Add($"tier={tier}");
            if (keys is not null) query.Add($"keys={keys}");
            return api.Get(Path(player) + (query.Count == 0 ? "" : "?" + string.Join("&", query)), serverKey: key);
        }

        static Dictionary<string, string> Data(Reply reply) =>
            reply.Json.GetProperty("data").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

        static Dictionary<string, string?> Set(params (string Key, string? Value)[] pairs) =>
            pairs.ToDictionary(p => p.Key, p => p.Value);

        // a brand new player, and how many rows they have in a tier
        async Task<Guid> NewPlayer() => (await api.RegisterGuest()).Id;

        Task<long> Rows(Guid player, int tier) =>
            app.Scalar<long>($"select count(*) from player_data where player_id = '{player}' and tier = {tier}");

        // ---- writing and reading

        [Fact]
        public async Task A_value_written_with_the_server_key_reads_back_the_same()
        {
            var player = await NewPlayer();

            var write = await Write(player, "ReadOnly", new { data = Set(("Xp", "1200")) });
            var read = await Read(player, "ReadOnly", "Xp");

            Assert.Equal(HttpStatusCode.OK, write.Status);
            Assert.Equal(1, write.Json.GetProperty("updated").GetInt32());
            Assert.Equal(HttpStatusCode.OK, read.Status);
            Assert.Equal(new Dictionary<string, string> { ["Xp"] = "1200" }, Data(read));
        }

        [Fact]
        public async Task Repeating_the_same_write_changes_nothing_more()
        {
            var player = await NewPlayer();
            var body = new { data = Set(("Xp", "1200"), ("Level", "7")) };

            var first = await Write(player, "ReadOnly", body);
            var again = await Write(player, "ReadOnly", body);

            Assert.Equal(HttpStatusCode.OK, again.Status);
            Assert.Equal(first.Body, again.Body);
            Assert.Equal(new Dictionary<string, string> { ["Xp"] = "1200", ["Level"] = "7" }, Data(await Read(player, "ReadOnly")));
            Assert.Equal(2, await Rows(player, 1));
        }

        [Fact]
        public async Task A_write_sets_the_value_and_never_adds_to_it()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Xp", "1200")) });

            await Write(player, "ReadOnly", new { data = Set(("Xp", "1300")) });

            Assert.Equal("1300", Data(await Read(player, "ReadOnly", "Xp"))["Xp"]);
        }

        [Fact]
        public async Task The_update_time_moves_forward_when_a_value_is_set_again()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Xp", "1")) });
            await app.Run($"update player_data set updated_at = now() - interval '1 hour' where player_id = '{player}'");

            await Write(player, "ReadOnly", new { data = Set(("Xp", "2")) });

            Assert.True(await app.Scalar<bool>($"select updated_at > now() - interval '1 minute' from player_data where player_id = '{player}' and key = 'Xp'"));
        }

        [Fact]
        public async Task A_value_can_be_any_text_and_comes_back_exactly()
        {
            var player = await NewPlayer();
            var tricky = new Dictionary<string, string?>
            {
                ["Json"] = "{\"a\":[1,2,3],\"b\":\"x\"}",
                ["Lines"] = "one\ntwo\r\nthree",
                ["Unicode"] = "名前 é 😀",
                ["Blank"] = "",
                ["Spaces"] = "  padded  ",
            };

            await Write(player, "ClientWritable", new { data = tricky });

            Assert.Equal(tricky, Data(await Read(player, "ClientWritable")).ToDictionary(p => p.Key, p => (string?)p.Value));
        }

        [Fact]
        public async Task The_key_keeps_its_case_in_the_answer()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Xp", "1"), ("xp", "2")) });

            var data = Data(await Read(player, "ReadOnly"));

            Assert.Equal("1", data["Xp"]);
            Assert.Equal("2", data["xp"]);
        }

        [Fact]
        public async Task A_missing_key_is_left_out_of_the_answer()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Xp", "1200")) });

            var read = await Read(player, "ReadOnly", "Xp,Nope");

            Assert.Equal(HttpStatusCode.OK, read.Status);
            Assert.Equal(new Dictionary<string, string> { ["Xp"] = "1200" }, Data(read));
            Assert.Empty(Data(await Read(player, "ReadOnly", "Nope")));
        }

        [Fact]
        public async Task With_no_keys_every_key_in_that_tier_comes_back()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Xp", "1200"), ("Level", "7"), ("Foo", "bar")) });
            await Write(player, "Internal", new { data = Set(("Secret", "s")) });

            var read = await Read(player, "ReadOnly");

            Assert.Equal(new Dictionary<string, string> { ["Xp"] = "1200", ["Level"] = "7", ["Foo"] = "bar" }, Data(read));
        }

        [Fact]
        public async Task A_player_with_no_data_gets_an_empty_answer()
        {
            var player = await NewPlayer();

            var read = await Read(player, "ReadOnly");

            Assert.Equal(HttpStatusCode.OK, read.Status);
            Assert.Empty(Data(read));
        }

        [Fact]
        public async Task The_tiers_are_separate_even_for_the_same_key()
        {
            var player = await NewPlayer();
            await Write(player, "ClientWritable", new { data = Set(("Xp", "from client")) });
            await Write(player, "ReadOnly", new { data = Set(("Xp", "from server")) });
            await Write(player, "Internal", new { data = Set(("Xp", "private")) });

            Assert.Equal("from client", Data(await Read(player, "ClientWritable"))["Xp"]);
            Assert.Equal("from server", Data(await Read(player, "ReadOnly"))["Xp"]);
            Assert.Equal("private", Data(await Read(player, "Internal"))["Xp"]);
        }

        [Fact]
        public async Task Players_do_not_see_each_others_data()
        {
            var one = await NewPlayer();
            var two = await NewPlayer();
            await Write(one, "ReadOnly", new { data = Set(("Xp", "1")) });

            Assert.Empty(Data(await Read(two, "ReadOnly")));
        }

        // ---- removing

        [Fact]
        public async Task Keys_can_be_removed_and_removing_a_missing_key_is_fine()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("A", "1"), ("B", "2"), ("C", "3")) });

            var remove = await Write(player, "ReadOnly", new { keysToRemove = new[] { "A", "NeverExisted" } });

            Assert.Equal(HttpStatusCode.OK, remove.Status);
            // one real removal, the other key wasn't there
            Assert.Equal(1, remove.Json.GetProperty("updated").GetInt32());
            Assert.Equal(new Dictionary<string, string> { ["B"] = "2", ["C"] = "3" }, Data(await Read(player, "ReadOnly")));
        }

        [Fact]
        public async Task One_request_can_set_and_remove_together()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Old", "1")) });

            var both = await Write(player, "ReadOnly", new { data = Set(("New", "2")), keysToRemove = new[] { "Old" } });

            Assert.Equal(2, both.Json.GetProperty("updated").GetInt32());
            Assert.Equal(new Dictionary<string, string> { ["New"] = "2" }, Data(await Read(player, "ReadOnly")));
        }

        [Fact]
        public async Task Removing_only_touches_the_tier_asked_for()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Xp", "1")) });
            await Write(player, "Internal", new { data = Set(("Xp", "2")) });

            await Write(player, "ReadOnly", new { keysToRemove = new[] { "Xp" } });

            Assert.Empty(Data(await Read(player, "ReadOnly")));
            Assert.Equal("2", Data(await Read(player, "Internal"))["Xp"]);
        }

        [Fact]
        public async Task A_request_with_nothing_in_it_changes_nothing()
        {
            var player = await NewPlayer();

            var reply = await Write(player, "ReadOnly", new { });

            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(0, reply.Json.GetProperty("updated").GetInt32());
        }

        // ---- the limits

        [Theory]
        [InlineData("bad key")]
        [InlineData("bad/key")]
        [InlineData("bad,key")]
        [InlineData("bad\\nkey")]
        [InlineData("é")]
        [InlineData("")]
        public async Task A_key_with_the_wrong_characters_is_refused(string badKey)
        {
            var player = await NewPlayer();

            var reply = await Write(player, "ReadOnly", new { data = Set((badKey, "v")) });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(ErrorMessages.PlayerDataKeyInvalid, reply.Error);
        }

        [Fact]
        public async Task A_key_of_64_characters_is_fine_and_65_is_refused()
        {
            var player = await NewPlayer();

            var ok = await Write(player, "ReadOnly", new { data = Set((new string('k', Limits.PlayerDataKeyMaxLength), "v")) });
            var tooLong = await Write(player, "ReadOnly", new { data = Set((new string('k', Limits.PlayerDataKeyMaxLength + 1), "v")) });

            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.Status);
            Assert.Equal(ErrorMessages.PlayerDataKeyInvalid, tooLong.Error);
        }

        [Fact]
        public async Task A_bad_key_to_remove_is_refused_too()
        {
            var player = await NewPlayer();

            var reply = await Write(player, "ReadOnly", new { keysToRemove = new[] { "bad key" } });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(ErrorMessages.PlayerDataKeyInvalid, reply.Error);
        }

        [Fact]
        public async Task A_value_of_16_KB_is_fine_and_one_byte_more_is_refused()
        {
            var player = await NewPlayer();

            var ok = await Write(player, "ReadOnly", new { data = Set(("Big", new string('v', Limits.PlayerDataValueMaxBytes))) });
            var tooBig = await Write(player, "ReadOnly", new { data = Set(("Big2", new string('v', Limits.PlayerDataValueMaxBytes + 1))) });

            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal(HttpStatusCode.BadRequest, tooBig.Status);
            Assert.Equal(ErrorMessages.PlayerDataValueInvalid, tooBig.Error);
            Assert.Equal(Limits.PlayerDataValueMaxBytes, Data(await Read(player, "ReadOnly", "Big"))["Big"].Length);
        }

        [Fact]
        public async Task The_value_limit_counts_bytes_so_multi_byte_text_hits_it_sooner()
        {
            var player = await NewPlayer();
            var half = Limits.PlayerDataValueMaxBytes / 2;

            var ok = await Write(player, "ReadOnly", new { data = Set(("Fits", new string('é', half))) });
            var tooBig = await Write(player, "ReadOnly", new { data = Set(("Over", new string('é', half + 1))) });

            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal(HttpStatusCode.BadRequest, tooBig.Status);
        }

        [Fact]
        public async Task A_null_value_is_refused_and_removing_is_done_with_keysToRemove()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Xp", "1")) });

            var reply = await Write(player, "ReadOnly", new { data = Set(("Xp", null)) });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(ErrorMessages.PlayerDataValueInvalid, reply.Error);
            Assert.Equal("1", Data(await Read(player, "ReadOnly"))["Xp"]);
        }

        [Fact]
        public async Task A_value_with_a_null_character_is_refused_and_not_a_server_error()
        {
            var player = await NewPlayer();

            var reply = await Write(player, "ReadOnly", new { data = Set(("Xp", "a\0b")) });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(ErrorMessages.PlayerDataValueInvalid, reply.Error);
        }

        [Fact]
        public async Task Ten_changes_in_one_request_are_fine_and_eleven_are_refused()
        {
            var player = await NewPlayer();
            var ten = Enumerable.Range(1, Limits.PlayerDataMaxChangesPerRequest).Select(i => ($"Ok{i}", (string?)"v")).ToArray();
            var eleven = Enumerable.Range(1, Limits.PlayerDataMaxChangesPerRequest + 1).Select(i => ($"Over{i}", (string?)"v")).ToArray();

            var ok = await Write(player, "ReadOnly", new { data = Set(ten) });
            var over = await Write(player, "ReadOnly", new { data = Set(eleven) });

            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal(Limits.PlayerDataMaxChangesPerRequest, ok.Json.GetProperty("updated").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest, over.Status);
            Assert.Equal(ErrorMessages.PlayerDataTooManyChanges, over.Error);
        }

        [Fact]
        public async Task Sets_and_removals_count_together_toward_the_ten()
        {
            var player = await NewPlayer();
            var sets = Enumerable.Range(1, 6).Select(i => ($"S{i}", (string?)"v")).ToArray();
            var removes = Enumerable.Range(1, 5).Select(i => $"R{i}").ToArray();

            var reply = await Write(player, "ReadOnly", new { data = Set(sets), keysToRemove = removes });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(ErrorMessages.PlayerDataTooManyChanges, reply.Error);
        }

        [Fact]
        public async Task A_key_set_and_removed_in_the_same_request_is_refused()
        {
            var player = await NewPlayer();

            var reply = await Write(player, "ReadOnly", new { data = Set(("Xp", "1")), keysToRemove = new[] { "Xp" } });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(ErrorMessages.PlayerDataKeyTwice, reply.Error);
        }

        [Fact]
        public async Task A_player_can_have_100_keys_in_a_tier_and_the_101st_is_refused()
        {
            var player = await NewPlayer();
            await Fill(player, "Internal", Limits.PlayerDataMaxKeysPerTier);

            var extra = await Write(player, "Internal", new { data = Set(("OneTooMany", "v")) });

            Assert.Equal(HttpStatusCode.BadRequest, extra.Status);
            Assert.Equal(ErrorMessages.PlayerDataTooManyKeys, extra.Error);
            Assert.Equal(Limits.PlayerDataMaxKeysPerTier, await Rows(player, 2));
            // the limit is per tier, so another tier is still open
            Assert.Equal(HttpStatusCode.OK, (await Write(player, "ReadOnly", new { data = Set(("Fine", "v")) })).Status);
        }

        [Fact]
        public async Task At_100_keys_changing_an_existing_key_or_swapping_one_for_one_is_still_fine()
        {
            var player = await NewPlayer();
            await Fill(player, "Internal", Limits.PlayerDataMaxKeysPerTier);

            var update = await Write(player, "Internal", new { data = Set(("N1", "new")) });
            var swap = await Write(player, "Internal", new { data = Set(("Extra", "v")), keysToRemove = new[] { "N2" } });
            var removeOne = await Write(player, "Internal", new { keysToRemove = new[] { "N3" } });
            var addOne = await Write(player, "Internal", new { data = Set(("Another", "v")) });

            Assert.Equal(HttpStatusCode.OK, update.Status);
            Assert.Equal(HttpStatusCode.OK, swap.Status);
            Assert.Equal(HttpStatusCode.OK, removeOne.Status);
            Assert.Equal(HttpStatusCode.OK, addOne.Status);
            Assert.Equal(Limits.PlayerDataMaxKeysPerTier, await Rows(player, 2));
        }

        // makes n keys N1..Nn in a tier, using the limit of 10 changes a request
        async Task Fill(Guid player, string tier, int n)
        {
            for (var start = 1; start <= n; start += Limits.PlayerDataMaxChangesPerRequest)
            {
                var batch = Enumerable.Range(start, Math.Min(Limits.PlayerDataMaxChangesPerRequest, n - start + 1)).Select(i => ($"N{i}", (string?)"v")).ToArray();
                Assert.Equal(HttpStatusCode.OK, (await Write(player, tier, new { data = Set(batch) })).Status);
            }
        }

        // ---- a refused request writes nothing

        [Fact]
        public async Task A_request_refused_for_one_bad_key_writes_none_of_its_good_keys()
        {
            var player = await NewPlayer();

            var reply = await Write(player, "ReadOnly", new { data = Set(("Good1", "v"), ("Good2", "v"), ("bad key", "v")) });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(0, await Rows(player, 1));
        }

        [Fact]
        public async Task A_request_refused_for_too_many_changes_writes_nothing()
        {
            var player = await NewPlayer();
            var eleven = Enumerable.Range(1, Limits.PlayerDataMaxChangesPerRequest + 1).Select(i => ($"K{i}", (string?)"v")).ToArray();

            await Write(player, "ReadOnly", new { data = Set(eleven) });

            Assert.Equal(0, await Rows(player, 1));
        }

        [Fact]
        public async Task A_request_refused_by_the_key_limit_writes_and_removes_nothing()
        {
            var player = await NewPlayer();
            await Fill(player, "Internal", Limits.PlayerDataMaxKeysPerTier);

            // two new keys would make 102, even though one is removed (so 101)
            var reply = await Write(player, "Internal", new { data = Set(("NewA", "v"), ("NewB", "v")), keysToRemove = new[] { "N1" } });

            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            var data = Data(await Read(player, "Internal"));
            Assert.Equal(Limits.PlayerDataMaxKeysPerTier, data.Count);
            Assert.Contains("N1", data.Keys);
            Assert.DoesNotContain("NewA", data.Keys);
            Assert.DoesNotContain("NewB", data.Keys);
        }

        [Fact]
        public async Task Writes_arriving_together_cannot_push_a_player_past_100_keys()
        {
            var player = await NewPlayer();
            await Fill(player, "Internal", Limits.PlayerDataMaxKeysPerTier - 10);

            // twenty writes at once, each adding a different key, with room for ten
            var replies = await Task.WhenAll(Enumerable.Range(0, 20)
                .Select(i => Write(player, "Internal", new { data = Set(($"Race{i}", "v")) })));

            Assert.Equal(10, replies.Count(r => r.Status == HttpStatusCode.OK));
            Assert.Equal(10, replies.Count(r => r.Status == HttpStatusCode.BadRequest));
            Assert.Equal(Limits.PlayerDataMaxKeysPerTier, await Rows(player, 2));
        }

        // ---- the tier is required

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("0")]
        [InlineData("Public")]
        [InlineData("Read Only")]
        public async Task A_missing_or_unknown_tier_is_refused_on_both_routes(string? tier)
        {
            var player = await NewPlayer();

            var read = await Read(player, tier);
            var write = await Write(player, tier, new { data = Set(("Xp", "1")) });

            Assert.Equal(HttpStatusCode.BadRequest, read.Status);
            Assert.Equal(ErrorMessages.PlayerDataTierInvalid, read.Error);
            Assert.Equal(HttpStatusCode.BadRequest, write.Status);
            Assert.Equal(ErrorMessages.PlayerDataTierInvalid, write.Error);
            Assert.Equal(0, await Rows(player, 1));
        }

        [Fact]
        public async Task The_tier_message_names_the_three_tiers()
        {
            var player = await NewPlayer();

            var reply = await Read(player, null);

            Assert.Equal("tier must be ClientWritable, ReadOnly or Internal", reply.Error);
        }

        [Theory]
        [InlineData("ReadOnly")]
        [InlineData("readonly")]
        [InlineData("READONLY")]
        public async Task The_tier_name_works_in_any_case_on_both_routes(string tier)
        {
            var player = await NewPlayer();

            var write = await Write(player, tier, new { data = Set(("Xp", "1")) });
            var read = await Read(player, tier, "Xp");

            Assert.Equal(HttpStatusCode.OK, write.Status);
            Assert.Equal("1", Data(read)["Xp"]);
            Assert.Equal(1, await Rows(player, 1));
        }

        [Fact]
        public async Task A_bad_keys_parameter_on_a_read_is_refused_and_not_treated_as_all()
        {
            var player = await NewPlayer();
            await Write(player, "ReadOnly", new { data = Set(("Xp", "1")) });

            foreach (var bad in new[] { "", "Xp,,Foo", "bad key", "," })
            {
                var reply = await Read(player, "ReadOnly", bad);
                Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
                Assert.Equal(ErrorMessages.PlayerDataKeyInvalid, reply.Error);
            }
        }

        // ---- who and which player

        [Fact]
        public async Task An_unknown_player_gets_404_on_both_routes()
        {
            var nobody = Guid.NewGuid();

            var read = await Read(nobody, "ReadOnly");
            var write = await Write(nobody, "ReadOnly", new { data = Set(("Xp", "1")) });

            Assert.Equal(HttpStatusCode.NotFound, read.Status);
            Assert.Equal(ErrorMessages.NoSuchPlayer, read.Error);
            Assert.Equal(HttpStatusCode.NotFound, write.Status);
            Assert.Equal(ErrorMessages.NoSuchPlayer, write.Error);
            Assert.Equal(0, await app.Scalar<long>($"select count(*) from player_data where player_id = '{nobody}'"));
        }

        [Fact]
        public async Task An_id_that_is_not_a_guid_gets_404()
        {
            var read = await api.Get("/api/server/players/not-a-guid/data?tier=ReadOnly", serverKey: key);
            var write = await api.Post("/api/server/players/not-a-guid/data?tier=ReadOnly", new { data = Set(("Xp", "1")) }, serverKey: key);

            Assert.Equal(HttpStatusCode.NotFound, read.Status);
            Assert.Equal(HttpStatusCode.NotFound, write.Status);
        }

        [Fact]
        public async Task Without_a_server_key_both_routes_are_401_and_nothing_is_written()
        {
            var player = await NewPlayer();

            var read = await api.Get($"{Path(player)}?tier=ReadOnly");
            var write = await api.Post($"{Path(player)}?tier=ReadOnly", new { data = Set(("Xp", "1")) });

            Assert.Equal(HttpStatusCode.Unauthorized, read.Status);
            Assert.Equal(HttpStatusCode.Unauthorized, write.Status);
            Assert.Equal(0, await Rows(player, 1));
        }

        [Fact]
        public async Task A_wrong_key_is_401()
        {
            var player = await NewPlayer();

            var read = await api.Get($"{Path(player)}?tier=ReadOnly", serverKey: "not-a-real-key");
            var write = await api.Post($"{Path(player)}?tier=ReadOnly", new { data = Set(("Xp", "1")) }, serverKey: "not-a-real-key");

            Assert.Equal(HttpStatusCode.Unauthorized, read.Status);
            Assert.Equal(HttpStatusCode.Unauthorized, write.Status);
        }

        [Fact]
        public async Task A_revoked_server_key_stops_working_on_these_routes()
        {
            var player = await NewPlayer();
            var owner = app.Api();
            await owner.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            var made = await owner.Post("/api/admin/server-keys", new { name = "to-revoke" });
            var revokedKey = made.Json.GetProperty("key").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await api.Get($"{Path(player)}?tier=ReadOnly", serverKey: revokedKey)).Status);

            await owner.Delete($"/api/admin/server-keys/{made.Json.GetProperty("id").GetInt64()}");

            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Get($"{Path(player)}?tier=ReadOnly", serverKey: revokedKey)).Status);
        }

        [Fact]
        public async Task A_player_token_is_not_a_server_key_here_either()
        {
            var guest = await api.RegisterGuest();

            var asBearer = await api.Get($"{Path(guest.Id)}?tier=ReadOnly", token: guest.Token);
            var asKey = await api.Post($"{Path(guest.Id)}?tier=ReadOnly", new { data = Set(("Xp", "999999")) }, serverKey: guest.Token);

            Assert.Equal(HttpStatusCode.Unauthorized, asBearer.Status);
            Assert.Equal(HttpStatusCode.Unauthorized, asKey.Status);
            Assert.Equal(0, await Rows(guest.Id, 1));
        }

        // ---- two players

        [Fact]
        public async Task The_data_of_two_players_is_kept_apart_in_the_table()
        {
            var one = await NewPlayer();
            var two = await NewPlayer();

            await Write(one, "ReadOnly", new { data = Set(("Xp", "1")) });
            await Write(two, "ReadOnly", new { data = Set(("Xp", "2")) });

            Assert.Equal("1", Data(await Read(one, "ReadOnly"))["Xp"]);
            Assert.Equal("2", Data(await Read(two, "ReadOnly"))["Xp"]);
        }
    }
}
