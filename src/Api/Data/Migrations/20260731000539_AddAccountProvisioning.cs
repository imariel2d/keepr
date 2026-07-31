using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Keepr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                schema: "keepr",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                schema: "keepr",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                schema: "keepr",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                schema: "keepr",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AccountInvites",
                schema: "keepr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountInvites_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "keepr",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_TokenHash",
                schema: "keepr",
                table: "AccountInvites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_UserId",
                schema: "keepr",
                table: "AccountInvites",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove invited-but-unclaimed accounts before restoring the NOT NULL constraint. Their
            // PasswordHash is NULL — the "no password yet, must claim" marker. Without this, the
            // AlterColumn below backfills that NULL with '' (defaultValue), turning each into an
            // ordinary account with an invalid hash: BCrypt.Verify(pw, "") throws, so its logins
            // become 500s instead of 401s, and it can never be claimed (AccountInvites is dropped
            // just below). These rows only exist because of this feature, so rolling it back should
            // delete them. See docs/feature-36-account-provisioning.md §8.1.
            migrationBuilder.Sql("DELETE FROM keepr.\"Users\" WHERE \"PasswordHash\" IS NULL;");

            migrationBuilder.DropTable(
                name: "AccountInvites",
                schema: "keepr");

            migrationBuilder.DropColumn(
                name: "FirstName",
                schema: "keepr",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastName",
                schema: "keepr",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                schema: "keepr",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                schema: "keepr",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
