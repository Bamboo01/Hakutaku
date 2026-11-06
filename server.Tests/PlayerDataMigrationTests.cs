/*!************************************************************************
\file   PlayerDataMigrationTests.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the tests for the migration that moves player XP
        into the player_data table. Each test builds the old schema, puts
        players with XP in it, runs the migration, and checks that every
        non-zero XP became a ReadOnly Xp key, that the Xp column is gone,
        and that the table's rules hold. A last test runs the migration
        backwards and checks the XP comes back.

        Runs the real migrations through EF, one at a time, on a database
        made by ScratchDatabase. Tests MovePlayerXpToPlayerData.
**************************************************************************/
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using server.Models;
using server.Tests.Support;

namespace server.Tests
{
    [Trait("Category", "Integration")]
    public class PlayerDataMigrationTests
    {
        // the last migration before the one under test, so the Players table still has its Xp column
        const string Before = "20261031140520_AddServerKeys";
        const string Move = "20261102104420_MovePlayerXpToPlayerData";

        // an empty database, migrated up to just before the XP move, with the given players in it
        static async Task<(ScratchDatabase Scratch, Db Db, IMigrator Migrator)> Start(params (Guid Id, int Xp)[] players)
        {
            var scratch = await ScratchDatabase.Create();
            var db = new Db(new DbContextOptionsBuilder<Db>().UseNpgsql(scratch.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(Before);
            foreach (var (id, xp) in players)
                await scratch.Run($"insert into \"Players\" (\"Id\", \"Xp\") values ('{id}', {xp})");
            return (scratch, db, migrator);
        }

        [Fact]
        public async Task Every_non_zero_xp_becomes_a_read_only_Xp_key_and_zero_gets_no_row()
        {
            var (bob, ana, cy, dee) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var (scratch, db, migrator) = await Start((bob, 1200), (ana, 0), (cy, 350), (dee, -5));
            await using var _s = scratch;
            await using var _d = db;

            await migrator.MigrateAsync(Move);

            // tier 1 is ReadOnly, and the value is the number written as text
            Assert.Equal("1200", await scratch.Scalar<string>($"select value from player_data where player_id = '{bob}' and tier = 1 and key = 'Xp'"));
            Assert.Equal("350", await scratch.Scalar<string>($"select value from player_data where player_id = '{cy}' and tier = 1 and key = 'Xp'"));
            Assert.Equal("-5", await scratch.Scalar<string>($"select value from player_data where player_id = '{dee}' and tier = 1 and key = 'Xp'"));
            Assert.Equal(0, await scratch.Scalar<long>($"select count(*) from player_data where player_id = '{ana}'"));
            // nothing else was made, and the copied rows say when
            Assert.Equal(3, await scratch.Scalar<long>("select count(*) from player_data"));
            Assert.Equal(3, await scratch.Scalar<long>("select count(*) from player_data where updated_at > now() - interval '5 minutes'"));
        }

        [Fact]
        public async Task The_Xp_column_is_gone_but_every_player_is_still_there()
        {
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var (scratch, db, migrator) = await Start((ids[0], 500), (ids[1], 0), (ids[2], 42));
            await using var _s = scratch;
            await using var _d = db;

            await migrator.MigrateAsync(Move);

            Assert.Equal(0, await scratch.Scalar<long>("select count(*) from information_schema.columns where table_name = 'Players' and column_name = 'Xp'"));
            Assert.Equal(3, await scratch.Scalar<long>("select count(*) from \"Players\""));
        }

        [Fact]
        public async Task A_database_with_no_players_migrates_fine()
        {
            var (scratch, db, migrator) = await Start();
            await using var _s = scratch;
            await using var _d = db;

            await migrator.MigrateAsync(Move);

            Assert.Equal(0, await scratch.Scalar<long>("select count(*) from player_data"));
        }

        [Fact]
        public async Task The_tier_must_be_0_1_or_2()
        {
            var player = Guid.NewGuid();
            var (scratch, db, migrator) = await Start((player, 0));
            await using var _s = scratch;
            await using var _d = db;
            await migrator.MigrateAsync(Move);

            foreach (var ok in new[] { 0, 1, 2 })
                await scratch.Run($"insert into player_data values ('{player}', {ok}, 'K', 'v', now())");

            var e = await Assert.ThrowsAsync<PostgresException>(() => scratch.Run($"insert into player_data values ('{player}', 3, 'K', 'v', now())"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, e.SqlState);
        }

        [Fact]
        public async Task A_player_has_one_value_per_key_in_each_tier()
        {
            var player = Guid.NewGuid();
            var (scratch, db, migrator) = await Start((player, 0));
            await using var _s = scratch;
            await using var _d = db;
            await migrator.MigrateAsync(Move);
            await scratch.Run($"insert into player_data values ('{player}', 1, 'K', 'v', now())");

            // the same key in another tier is a different entry, the same key in the same tier is not
            await scratch.Run($"insert into player_data values ('{player}', 0, 'K', 'v', now())");
            var e = await Assert.ThrowsAsync<PostgresException>(() => scratch.Run($"insert into player_data values ('{player}', 1, 'K', 'other', now())"));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, e.SqlState);
        }

        [Fact]
        public async Task Player_data_must_belong_to_a_real_player_and_goes_when_the_player_goes()
        {
            var player = Guid.NewGuid();
            var (scratch, db, migrator) = await Start((player, 700));
            await using var _s = scratch;
            await using var _d = db;
            await migrator.MigrateAsync(Move);

            var e = await Assert.ThrowsAsync<PostgresException>(() => scratch.Run($"insert into player_data values ('{Guid.NewGuid()}', 1, 'K', 'v', now())"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, e.SqlState);

            Assert.Equal(1, await scratch.Scalar<long>($"select count(*) from player_data where player_id = '{player}'"));
            await scratch.Run($"delete from \"Players\" where \"Id\" = '{player}'");
            Assert.Equal(0, await scratch.Scalar<long>($"select count(*) from player_data where player_id = '{player}'"));
        }

        [Fact]
        public async Task Running_it_backwards_puts_the_xp_back_and_drops_the_table()
        {
            var (bob, ana, cy, dee) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var (scratch, db, migrator) = await Start((bob, 1200), (ana, 0), (cy, 350), (dee, -5));
            await using var _s = scratch;
            await using var _d = db;
            await migrator.MigrateAsync(Move);

            await migrator.MigrateAsync(Before);

            Assert.Equal(1200, await scratch.Scalar<int>($"select \"Xp\" from \"Players\" where \"Id\" = '{bob}'"));
            Assert.Equal(0, await scratch.Scalar<int>($"select \"Xp\" from \"Players\" where \"Id\" = '{ana}'"));
            Assert.Equal(350, await scratch.Scalar<int>($"select \"Xp\" from \"Players\" where \"Id\" = '{cy}'"));
            Assert.Equal(-5, await scratch.Scalar<int>($"select \"Xp\" from \"Players\" where \"Id\" = '{dee}'"));
            Assert.Equal(0, await scratch.Scalar<long>("select count(*) from information_schema.tables where table_name = 'player_data'"));
        }

        [Fact]
        public async Task Running_it_backwards_does_not_fail_on_an_xp_that_is_not_a_number()
        {
            var player = Guid.NewGuid();
            var (scratch, db, migrator) = await Start((player, 0));
            await using var _s = scratch;
            await using var _d = db;
            await migrator.MigrateAsync(Move);
            // the game server can write any text, so this one can't go back into an integer column
            await scratch.Run($"insert into player_data values ('{player}', 1, 'Xp', 'a lot', now())");

            await migrator.MigrateAsync(Before);

            Assert.Equal(0, await scratch.Scalar<int>($"select \"Xp\" from \"Players\" where \"Id\" = '{player}'"));
        }
    }
}
