using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Keepr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleLiveInvite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountInvites_UserId",
                schema: "keepr",
                table: "AccountInvites");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_UserId",
                schema: "keepr",
                table: "AccountInvites",
                column: "UserId",
                unique: true,
                filter: "\"ClaimedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountInvites_UserId",
                schema: "keepr",
                table: "AccountInvites");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_UserId",
                schema: "keepr",
                table: "AccountInvites",
                column: "UserId");
        }
    }
}
