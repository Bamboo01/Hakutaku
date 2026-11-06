using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations
{
    /// <summary>
    /// Player XP moves out of the Players.Xp column into player_data, as a ReadOnly "Xp" key. The
    /// steps run in this order so nobody loses any XP: make the table, copy every non-zero Xp into it,
    /// and only then drop the column. A player with 0 XP gets no row, and a missing key just means
    /// none yet.
    /// </summary>
    public partial class MovePlayerXpToPlayerData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_data",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<short>(type: "smallint", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_data", x => new { x.player_id, x.tier, x.key });
                    table.CheckConstraint("ck_player_data_tier", "tier IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_player_data_Players_player_id",
                        column: x => x.player_id,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // tier 1 is ReadOnly. the copy has to come before the drop below
            migrationBuilder.Sql("""
                INSERT INTO player_data (player_id, tier, key, value, updated_at)
                SELECT "Id", 1, 'Xp', "Xp"::text, now() FROM "Players" WHERE "Xp" <> 0;
                """);

            migrationBuilder.DropColumn(
                name: "Xp",
                table: "Players");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Xp",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // best effort: put each ReadOnly Xp back. a value that isn't a plain whole number that fits in
            // an int (the game server can write any text) can't go back into the column, so it comes back as 0
            migrationBuilder.Sql("""
                UPDATE "Players" p
                SET "Xp" = d.value::integer
                FROM player_data d
                WHERE d.player_id = p."Id" AND d.tier = 1 AND d.key = 'Xp' AND d.value ~ '^-?[0-9]{1,9}$';
                """);

            migrationBuilder.DropTable(
                name: "player_data");
        }
    }
}
