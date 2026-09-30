using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameToAdminUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "admin_users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "admin_users",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Existing rows (the seeded owner) used email as the login identifier;
            // carry that value over so nobody's account stops working.
            migrationBuilder.Sql("UPDATE admin_users SET username = email WHERE username = '';");

            migrationBuilder.Sql("DROP INDEX ux_admin_users_email_lower;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_admin_users_username_lower ON admin_users (lower(username));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ux_admin_users_username_lower;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_admin_users_email_lower ON admin_users (lower(email));");

            migrationBuilder.DropColumn(
                name: "username",
                table: "admin_users");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "admin_users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
