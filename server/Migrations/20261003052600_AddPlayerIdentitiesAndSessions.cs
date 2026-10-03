using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace server.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerIdentitiesAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_identities",
                columns: table => new
                {
                    provider = table.Column<short>(type: "smallint", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    pw_hash = table.Column<string>(type: "text", nullable: true),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_identities", x => new { x.provider, x.subject });
                    table.CheckConstraint("ck_player_identities_provider", "provider IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_player_identities_Players_player_id",
                        column: x => x.player_id,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_player_sessions_Players_player_id",
                        column: x => x.player_id,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_player_identities_one_email",
                table: "player_identities",
                column: "player_id",
                unique: true,
                filter: "provider = 1");

            migrationBuilder.CreateIndex(
                name: "IX_player_sessions_player_id",
                table: "player_sessions",
                column: "player_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_player_sessions_token_hash",
                table: "player_sessions",
                column: "token_hash",
                unique: true);

            // Every existing player's device ID becomes a device identity, so nobody is
            // locked out of their account by the column going away. EF generated the
            // drop of DeviceId first; it has to come after this copy.
            migrationBuilder.Sql(
                @"INSERT INTO player_identities (player_id, provider, subject)
                  SELECT ""Id"", 0, ""DeviceId"" FROM ""Players"" WHERE ""DeviceId"" <> ''
                  ON CONFLICT (provider, subject) DO NOTHING;");

            migrationBuilder.DropIndex(
                name: "IX_Players_DeviceId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "Players");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "Players",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Restore each player's device ID from their device identity. A player with
            // none (email-only, or made by an admin without one) gets a placeholder, so
            // the unique index below can still be built.
            migrationBuilder.Sql(
                @"UPDATE ""Players"" p SET ""DeviceId"" = i.subject
                  FROM player_identities i
                  WHERE i.player_id = p.""Id"" AND i.provider = 0;");
            migrationBuilder.Sql(
                @"UPDATE ""Players"" SET ""DeviceId"" = 'restored-' || ""Id""::text WHERE ""DeviceId"" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Players_DeviceId",
                table: "Players",
                column: "DeviceId",
                unique: true);

            migrationBuilder.DropTable(
                name: "player_identities");

            migrationBuilder.DropTable(
                name: "player_sessions");
        }
    }
}
