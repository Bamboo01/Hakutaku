/*!************************************************************************
\file   HakutakuFactory.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines HakutakuFactory, which starts the real server in
        memory for the integration tests. Each factory makes its own empty
        Postgres database, so the server runs its migrations like on a
        fresh start, and drops the database when the tests are done. Mail
        goes to a CapturingEmailSender instead of the network.

        The Postgres server comes from the HAKUTAKU_TEST_PG environment
        variable, or the local dev compose database by default. Used by the
        integration test classes through IClassFixture.
**************************************************************************/
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using server.SEmail;

namespace server.Tests.Support
{
    // the db type is only there so the factory can find the server's assembly and its entry point
    public sealed class HakutakuFactory : WebApplicationFactory<server.Models.Db>
    {
        // the local dev database from compose.dev.yaml. point HAKUTAKU_TEST_PG at another server to use that instead
        const string DefaultServer = "Host=localhost;Port=5432;Username=hakutaku;Password=hakutaku";

        readonly string serverConnection = Environment.GetEnvironmentVariable("HAKUTAKU_TEST_PG") ?? DefaultServer;
        readonly string dbName = "hk_test_" + Guid.NewGuid().ToString("N");

        public string ConnectionString => $"{serverConnection};Database={dbName}";
        public CapturingEmailSender Mail { get; } = new();

        public HakutakuFactory()
        {
            using var conn = new NpgsqlConnection($"{serverConnection};Database=postgres");
            try
            {
                conn.Open();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    "The integration tests need a Postgres server. Start one with " +
                    "'docker compose -f compose.dev.yaml up -d postgres', or set HAKUTAKU_TEST_PG, " +
                    "or run only the unit tests with: dotnet test --filter \"Category!=Integration\"", e);
            }
            using var cmd = new NpgsqlCommand($"CREATE DATABASE {dbName}", conn);
            cmd.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(ServerDirectory());
            builder.UseSetting("ConnectionStrings:Db", ConnectionString);
            builder.UseSetting("HAKUTAKU_ADMIN_PASSWORD", "test-owner-password-1");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Mail);
            });
        }

        public ApiClient Api() => new(CreateClient());

        // a query that returns one value, for checking what the server wrote or for setting up a state
        public async Task<T?> Scalar<T>(string sql)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            var value = await cmd.ExecuteScalarAsync();
            return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
        }

        public async Task Run(string sql)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            NpgsqlConnection.ClearAllPools();
            using var conn = new NpgsqlConnection($"{serverConnection};Database=postgres");
            conn.Open();
            using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {dbName} WITH (FORCE)", conn);
            cmd.ExecuteNonQuery();
        }

        // the server's folder, found by walking up from the test build output
        static string ServerDirectory()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "server");
                if (File.Exists(Path.Combine(candidate, "Hakutaku.Server.csproj"))) return candidate;
            }
            throw new InvalidOperationException("Could not find the server folder above " + AppContext.BaseDirectory);
        }
    }
}
