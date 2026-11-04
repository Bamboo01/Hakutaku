/*!************************************************************************
\file   ServerKeyErrorLogTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   4th November 2026
\brief  This file defines the integration test for naming the server key in
        the log when a game server's call fails with a 500. It breaks the
        player_data table on purpose, so it has its own class and its own
        database, and puts the table back when it's done.

        Runs against the real server through HakutakuFactory. Tests the
        exception handler in Hosting/ApiErrors.cs together with
        RequireServerKeyFilter.
**************************************************************************/
using System.Net;
using server.Constants;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class ServerKeyErrorLogTests(HakutakuFactory app) : IClassFixture<HakutakuFactory>
    {
        [Fact]
        public async Task A_failed_server_call_logs_which_key_made_it()
        {
            var owner = app.Api();
            await owner.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            var made = await owner.Post("/api/admin/server-keys", new { name = "error-log" });
            var keyId = made.Json.GetProperty("id").GetInt64();
            var key = made.Json.GetProperty("key").GetString()!;
            var player = await app.Api().RegisterGuest();

            // the player exists, so the read gets as far as player_data, which is gone
            await app.Run("alter table player_data rename to player_data_moved");
            try
            {
                var reply = await app.Api().Get($"/api/server/players/{player.Id}/data?tier=ReadOnly", serverKey: key);

                Assert.Equal(HttpStatusCode.InternalServerError, reply.Status);
                Assert.Equal(ErrorMessages.ServerError, reply.Error);
                Assert.Contains(app.Logs.In(LogCategories.GameServer), l =>
                    l.Level == Microsoft.Extensions.Logging.LogLevel.Error
                    && l.Message.Contains($"Server key {keyId} ")
                    && l.Message.Contains($"/api/server/players/{player.Id}/data"));
            }
            finally
            {
                await app.Run("alter table player_data_moved rename to player_data");
            }
        }

        [Fact]
        public async Task A_failed_call_without_a_server_key_names_no_key()
        {
            var owner = app.Api();
            await owner.LoginAdmin("admin", HakutakuFactory.OwnerPassword);
            var before = app.Logs.In(LogCategories.GameServer).Count;

            // an admin route, so no server key is involved. the table it reads is gone
            await app.Run("alter table server_keys rename to server_keys_moved");
            try
            {
                var reply = await owner.Get("/api/admin/server-keys");

                Assert.Equal(HttpStatusCode.InternalServerError, reply.Status);
                Assert.Equal(before, app.Logs.In(LogCategories.GameServer).Count);
            }
            finally
            {
                await app.Run("alter table server_keys_moved rename to server_keys");
            }
        }
    }
}
