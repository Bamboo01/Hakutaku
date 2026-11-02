/*!************************************************************************
\file   ScratchDatabase.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines ScratchDatabase, an empty Postgres database that
        lives for one test. Unlike HakutakuFactory it does not start the
        server or run any migrations, so a test can run them itself, one at
        a time, and put data in between. It is dropped when the test is
        done.

        Used by PlayerDataMigrationTests. Connects to the same Postgres as
        HakutakuFactory, from HAKUTAKU_TEST_PG or the local dev database.
**************************************************************************/
using Npgsql;

namespace server.Tests.Support
{
    public sealed class ScratchDatabase : IAsyncDisposable
    {
        // the local dev database from compose.dev.yaml, same default as HakutakuFactory
        const string DefaultServer = "Host=localhost;Port=5432;Username=hakutaku;Password=hakutaku";

        readonly string serverConnection = Environment.GetEnvironmentVariable("HAKUTAKU_TEST_PG") ?? DefaultServer;
        readonly string dbName = "hk_scratch_" + Guid.NewGuid().ToString("N");

        public string ConnectionString => $"{serverConnection};Database={dbName}";

        public static async Task<ScratchDatabase> Create()
        {
            var scratch = new ScratchDatabase();
            await using var conn = new NpgsqlConnection($"{scratch.serverConnection};Database=postgres");
            try
            {
                await conn.OpenAsync();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    "The integration tests need a Postgres server. Start one with " +
                    "'docker compose -f compose.dev.yaml up -d postgres', or set HAKUTAKU_TEST_PG, " +
                    "or run only the unit tests with: dotnet test --filter \"Category!=Integration\"", e);
            }
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {scratch.dbName}", conn);
            await cmd.ExecuteNonQueryAsync();
            return scratch;
        }

        public async Task Run(string sql)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // a query that returns one value
        public async Task<T?> Scalar<T>(string sql)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            var value = await cmd.ExecuteScalarAsync();
            return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection($"{serverConnection};Database=postgres");
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {dbName} WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
