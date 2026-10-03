using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations
{
    /// <summary>
    /// Device logins move from a client-chosen device ID to a token the server mints, and
    /// only the token's SHA-256 is stored. Existing device rows are brought into line so
    /// nobody is locked out, and the new rule (a verified email means no device token) is
    /// applied to players who already have one.
    /// </summary>
    public partial class AddDeviceTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegisteredHardwareId",
                table: "Players",
                type: "text",
                nullable: true);

            // A verified email makes the account recoverable, so these players lose their
            // device identity, as email/verify now does. They sign in by email.
            migrationBuilder.Sql("""
                DELETE FROM player_identities d
                WHERE d.provider = 0
                  AND EXISTS (SELECT 1 FROM player_identities e
                              WHERE e.player_id = d.player_id AND e.provider = 1 AND e.verified_at IS NOT NULL);
                """);

            // The rest store the old device ID as its hash, in the same form as
            // PlayerAuth.HashDeviceToken (SHA-256 of the UTF-8 text, lowercase hex), so the
            // old ID keeps working as a device token with login/device.
            migrationBuilder.Sql("""
                UPDATE player_identities
                SET subject = encode(sha256(convert_to(subject, 'UTF8')), 'hex')
                WHERE provider = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The device rows now hold hashes, which can't be turned back into the device IDs
            // the old code looked up, so rolling back would lock every guest out.
            throw new NotSupportedException(
                "AddDeviceTokens can't be reverted: device identities are stored hashed and the old device IDs can't be recovered.");
        }
    }
}
