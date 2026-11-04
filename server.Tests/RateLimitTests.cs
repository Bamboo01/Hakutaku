/*!************************************************************************
\file   RateLimitTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   4th November 2026
\brief  This file defines the integration tests for the per-IP rate limits
        that differ from the default. Register and device login let a room
        of players behind one IP all start the game in the same minute,
        while email login keeps the strict default.

        Runs against the real server through HakutakuFactory. Tests the
        policies in Hosting/RateLimiting.cs with the numbers from
        Constants/RateLimits.cs.
**************************************************************************/
using System.Net;
using server.Constants;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class RateLimitTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>
    {
        readonly ApiClient api = app.Api();

        // one made-up address per test, kept apart from ApiClient's 10.x.x.x ones
        static int next;
        static string SharedIp() => $"192.168.77.{Interlocked.Increment(ref next)}";

        [Fact]
        public async Task Register_lets_a_room_on_one_ip_in_and_then_limits_it()
        {
            var ip = SharedIp();
            for (var i = 0; i < RateLimits.RegisterPermitLimit; i++)
                await api.RegisterGuest(ip);

            var over = await api.Post("/api/players/register", new { hardwareId = "test-over" }, ip: ip);
            Assert.Equal(HttpStatusCode.TooManyRequests, over.Status);
        }

        [Fact]
        public async Task Device_login_lets_a_room_on_one_ip_in_and_then_limits_it()
        {
            var ip = SharedIp();
            var guest = await api.RegisterGuest();
            for (var i = 0; i < RateLimits.DeviceLoginPermitLimit; i++)
                Assert.Equal(HttpStatusCode.OK, (await api.Post("/api/players/login/device", new { deviceToken = guest.DeviceToken }, ip: ip)).Status);

            var over = await api.Post("/api/players/login/device", new { deviceToken = guest.DeviceToken }, ip: ip);
            Assert.Equal(HttpStatusCode.TooManyRequests, over.Status);
        }

        [Fact]
        public async Task Email_login_keeps_the_strict_default()
        {
            var ip = SharedIp();
            for (var i = 0; i < RateLimits.PermitLimit; i++)
                await api.Post("/api/players/login/email", new { email = "nobody@example.com", password = "wrong-password-1" }, ip: ip);

            var over = await api.Post("/api/players/login/email", new { email = "nobody@example.com", password = "wrong-password-1" }, ip: ip);
            Assert.Equal(HttpStatusCode.TooManyRequests, over.Status);
        }
    }
}
