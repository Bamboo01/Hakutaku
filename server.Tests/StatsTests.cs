/*!************************************************************************
\file   StatsTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   16th November 2026
\brief  This file defines the integration tests for GET /api/admin/stats,
        the Overview page's numbers. A new guest counts as a new user, a
        login and a unique user in the last 24 hours; logins on earlier
        days land in the right day and period; one player logging in twice
        is two logins but one user; there are always 30 days, oldest first;
        and only an admin gets in.

        Runs against the real server through HakutakuFactory, and backdates
        sessions with SQL. Tests GetStats.cs and StatsService.cs. Every
        test reads the numbers before and after, since the tests in this
        class share a database.
**************************************************************************/
using System.Net;
using System.Text.Json;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class StatsTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>, IAsyncLifetime
    {
        readonly ApiClient admin = app.Api();
        readonly ApiClient anon = app.Api();

        public Task InitializeAsync() => admin.LoginAdmin("admin", HakutakuFactory.OwnerPassword);

        public Task DisposeAsync() => Task.CompletedTask;

        async Task<JsonElement> Stats()
        {
            var reply = await admin.Get("/api/admin/stats");
            Assert.Equal(HttpStatusCode.OK, reply.Status);
            return reply.Json;
        }

        static long Figure(JsonElement stats, string metric, string window) => stats.GetProperty(metric).GetProperty(window).GetInt64();

        // the bar for `daysAgo` (0 = the last 24 hours); days are oldest first
        static JsonElement Bar(JsonElement stats, int daysAgo)
        {
            var days = stats.GetProperty("days");
            return days[days.GetArrayLength() - 1 - daysAgo];
        }

        // a session for the player, issued `ago` before now
        Task Session(Guid player, TimeSpan ago) => app.Run($"""
            insert into player_sessions (player_id, token_hash, issued_at, expires_at)
            values ('{player}', decode(md5(random()::text || clock_timestamp()::text), 'hex'),
                    now() - interval '{(long)ago.TotalSeconds} seconds', now() + interval '1 day')
            """);

        [Fact]
        public async Task A_new_guest_is_a_new_user_a_login_and_a_unique_user_today()
        {
            var before = await Stats();

            await anon.RegisterGuest();
            var after = await Stats();

            foreach (var metric in new[] { "newUsers", "logins", "uniqueUsers" })
            {
                Assert.Equal(Figure(before, metric, "last24h") + 1, Figure(after, metric, "last24h"));
                Assert.Equal(Figure(before, metric, "last30Days") + 1, Figure(after, metric, "last30Days"));
            }
            Assert.Equal(before.GetProperty("totalPlayers").GetInt64() + 1, after.GetProperty("totalPlayers").GetInt64());
            Assert.Equal(Bar(before, 0).GetProperty("newUsers").GetInt64() + 1, Bar(after, 0).GetProperty("newUsers").GetInt64());
        }

        [Fact]
        public async Task A_login_three_days_ago_lands_on_that_day_and_in_the_30_days_but_not_today()
        {
            var guest = await anon.RegisterGuest();
            var before = await Stats();

            await Session(guest.Id, TimeSpan.FromDays(3.5));
            var after = await Stats();

            Assert.Equal(Bar(before, 3).GetProperty("logins").GetInt64() + 1, Bar(after, 3).GetProperty("logins").GetInt64());
            Assert.Equal(Bar(before, 3).GetProperty("uniqueUsers").GetInt64() + 1, Bar(after, 3).GetProperty("uniqueUsers").GetInt64());
            Assert.Equal(Figure(before, "logins", "last24h"), Figure(after, "logins", "last24h"));
            Assert.Equal(Figure(before, "logins", "last30Days") + 1, Figure(after, "logins", "last30Days"));
            // the player already logged in this month, when they registered, so they're not a new unique user
            Assert.Equal(Figure(before, "uniqueUsers", "last30Days"), Figure(after, "uniqueUsers", "last30Days"));
        }

        [Fact]
        public async Task Yesterday_and_the_month_before_count_as_the_previous_periods()
        {
            var guest = await anon.RegisterGuest();
            var before = await Stats();

            await Session(guest.Id, TimeSpan.FromHours(30));
            await Session(guest.Id, TimeSpan.FromDays(45));
            var after = await Stats();

            Assert.Equal(Figure(before, "logins", "previous24h") + 1, Figure(after, "logins", "previous24h"));
            Assert.Equal(Figure(before, "uniqueUsers", "previous24h") + 1, Figure(after, "uniqueUsers", "previous24h"));
            Assert.Equal(Figure(before, "logins", "previous30Days") + 1, Figure(after, "logins", "previous30Days"));
            Assert.Equal(Figure(before, "uniqueUsers", "previous30Days") + 1, Figure(after, "uniqueUsers", "previous30Days"));
        }

        [Fact]
        public async Task Two_logins_by_one_player_in_a_day_are_two_logins_but_one_user()
        {
            var guest = await anon.RegisterGuest();
            var before = await Stats();

            await Session(guest.Id, TimeSpan.FromDays(5.2));
            await Session(guest.Id, TimeSpan.FromDays(5.4));
            var after = await Stats();

            Assert.Equal(Bar(before, 5).GetProperty("logins").GetInt64() + 2, Bar(after, 5).GetProperty("logins").GetInt64());
            Assert.Equal(Bar(before, 5).GetProperty("uniqueUsers").GetInt64() + 1, Bar(after, 5).GetProperty("uniqueUsers").GetInt64());
        }

        [Fact]
        public async Task A_login_older_than_60_days_is_not_counted_anywhere()
        {
            var guest = await anon.RegisterGuest();
            var before = await Stats();

            await Session(guest.Id, TimeSpan.FromDays(75));
            var after = await Stats();

            Assert.Equal(Figure(before, "logins", "previous30Days"), Figure(after, "logins", "previous30Days"));
            Assert.Equal(Figure(before, "logins", "last30Days"), Figure(after, "logins", "last30Days"));
        }

        [Fact]
        public async Task There_are_always_30_days_oldest_first_ending_now()
        {
            var stats = await Stats();

            var days = stats.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("endsAt").GetDateTimeOffset()).ToList();
            var generated = stats.GetProperty("generatedAt").GetDateTimeOffset();
            Assert.Equal(30, days.Count);
            Assert.Equal(generated, days[^1]);
            Assert.Equal(generated.AddDays(-29), days[0]);
            Assert.True(days.Zip(days.Skip(1)).All(p => p.First < p.Second));
        }

        [Fact]
        public async Task Only_a_signed_in_admin_gets_the_numbers()
        {
            var guest = await anon.RegisterGuest();

            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Get("/api/admin/stats")).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Get("/api/admin/stats", token: guest.Token)).Status);
        }
    }
}
