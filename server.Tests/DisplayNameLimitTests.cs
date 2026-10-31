/*!************************************************************************
\file   DisplayNameLimitTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines the integration tests for the display name limit.
        A player can try to change their name a few times an hour. The
        count is per player and not per IP, refused names count too, and a
        signed-out call is a 401 that never uses anything up.

        Runs against the real server through HakutakuFactory. Tests
        DisplayNameLimiter through the display-name route.
**************************************************************************/
using System.Net;
using server.Constants;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class DisplayNameLimitTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>
    {
        readonly ApiClient api = app.Api();

        [Fact]
        public async Task The_try_after_the_allowance_is_refused_with_a_message()
        {
            var guest = await api.RegisterGuest();

            for (var i = 1; i <= RateLimits.DisplayNameChanges; i++)
                Assert.Equal(HttpStatusCode.OK, (await api.SetDisplayName(guest.Token, $"Name {i}")).Status);
            var over = await api.SetDisplayName(guest.Token, "One too many");

            Assert.Equal(HttpStatusCode.TooManyRequests, over.Status);
            Assert.Equal(ErrorMessages.DisplayNameRateLimited, over.Error);
        }

        [Fact]
        public async Task The_name_is_not_changed_by_a_refused_try()
        {
            var guest = await api.RegisterGuest();
            for (var i = 1; i <= RateLimits.DisplayNameChanges; i++)
                await api.SetDisplayName(guest.Token, "Allowed");

            await api.SetDisplayName(guest.Token, "Refused");

            Assert.Equal("Allowed", await app.Scalar<string>($"select \"DisplayName\" from \"Players\" where \"Id\" = '{guest.Id}'"));
        }

        [Fact]
        public async Task Refused_names_use_up_the_allowance_too()
        {
            var guest = await api.RegisterGuest();

            for (var i = 1; i <= RateLimits.DisplayNameChanges; i++)
                Assert.Equal(HttpStatusCode.BadRequest, (await api.SetDisplayName(guest.Token, "ab")).Status);

            Assert.Equal(HttpStatusCode.TooManyRequests, (await api.SetDisplayName(guest.Token, "Fine Name")).Status);
        }

        [Fact]
        public async Task Another_player_on_the_same_ip_is_not_affected()
        {
            const string sharedIp = "203.0.113.7";
            var busy = await api.RegisterGuest();
            var quiet = await api.RegisterGuest();

            for (var i = 0; i <= RateLimits.DisplayNameChanges; i++)
                await api.SetDisplayName(busy.Token, $"Busy {i}", sharedIp);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await api.SetDisplayName(busy.Token, "Busy again", sharedIp)).Status);

            Assert.Equal(HttpStatusCode.OK, (await api.SetDisplayName(quiet.Token, "Quiet", sharedIp)).Status);
        }

        [Fact]
        public async Task Signed_out_calls_are_401_and_never_use_anything_up()
        {
            for (var i = 0; i < RateLimits.DisplayNameChanges + 3; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await api.Post("/api/players/display-name", new { displayName = "Nobody" })).Status);
        }

        [Fact]
        public async Task Each_try_still_checks_the_name_rules()
        {
            var guest = await api.RegisterGuest();

            var ok = await api.SetDisplayName(guest.Token, "  Sir Bob  ");

            Assert.Equal(HttpStatusCode.OK, ok.Status);
            Assert.Equal("Sir Bob", ok.Json.GetProperty("displayName").GetString());
        }
    }
}
