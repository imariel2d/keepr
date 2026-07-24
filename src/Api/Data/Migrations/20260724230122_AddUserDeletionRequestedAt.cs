using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Keepr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDeletionRequestedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletionRequestedAt",
                schema: "keepr",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletionRequestedAt",
                schema: "keepr",
                table: "Users");
        }
    }
}
