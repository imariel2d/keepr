using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Keepr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreShareTokenForReCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing links used the hashed-token scheme, whose raw token was never stored — so
            // they cannot be carried into the token-stored scheme and would stop resolving anyway.
            // Drop them before the swap; this also avoids a duplicate empty-string collision when
            // the new NOT NULL Token column (default '') meets its unique index.
            migrationBuilder.Sql(@"DELETE FROM keepr.""ShareLinks"";");

            migrationBuilder.DropIndex(
                name: "IX_ShareLinks_TokenHash",
                schema: "keepr",
                table: "ShareLinks");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                schema: "keepr",
                table: "ShareLinks");

            migrationBuilder.AddColumn<string>(
                name: "Token",
                schema: "keepr",
                table: "ShareLinks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_Token",
                schema: "keepr",
                table: "ShareLinks",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShareLinks_Token",
                schema: "keepr",
                table: "ShareLinks");

            migrationBuilder.DropColumn(
                name: "Token",
                schema: "keepr",
                table: "ShareLinks");

            migrationBuilder.AddColumn<byte[]>(
                name: "TokenHash",
                schema: "keepr",
                table: "ShareLinks",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_TokenHash",
                schema: "keepr",
                table: "ShareLinks",
                column: "TokenHash",
                unique: true);
        }
    }
}
