using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Keepr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                schema: "keepr",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: an account with a *claimed* invite provably followed a link we emailed to its
            // address, so its inbox is verified. Everyone else stays false (direct-set, legacy #3
            // self-registered) and recovers via admin reset until verified another way. The bootstrap
            // admin is handled separately by AdminSeeder at startup (it can't be known from the DB
            // alone). See docs/feature-26-password-reset.md §3.3.
            migrationBuilder.Sql("""
                UPDATE keepr."Users" u
                SET "EmailVerified" = true
                WHERE EXISTS (
                    SELECT 1 FROM keepr."AccountInvites" i
                    WHERE i."UserId" = u."Id" AND i."ClaimedAt" IS NOT NULL);
                """);

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                schema: "keepr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "keepr",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                schema: "keepr",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                schema: "keepr",
                table: "PasswordResetTokens",
                column: "UserId",
                unique: true,
                filter: "\"UsedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResetTokens",
                schema: "keepr");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                schema: "keepr",
                table: "Users");
        }
    }
}
