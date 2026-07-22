using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Keepr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFoldersAndTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "OriginalName",
                schema: "keepr",
                table: "MediaFiles",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                schema: "keepr",
                table: "MediaFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedRootId",
                schema: "keepr",
                table: "MediaFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                schema: "keepr",
                table: "MediaFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalNameLower",
                schema: "keepr",
                table: "MediaFiles",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            // Backfill before the unique index below is created. Without this, every pre-existing
            // row keeps the "" default and any two of them collide on
            // (OwnerId, FolderId=null, OriginalNameLower="") the moment the index is added.
            migrationBuilder.Sql(
                """UPDATE keepr."MediaFiles" SET "OriginalNameLower" = lower("OriginalName");""");

            migrationBuilder.CreateTable(
                name: "Folders",
                schema: "keepr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NameLower = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedRootId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Folders_Folders_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "keepr",
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Folders_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "keepr",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_DeletedAt",
                schema: "keepr",
                table: "MediaFiles",
                column: "DeletedAt",
                filter: "\"DeletedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_FolderId",
                schema: "keepr",
                table: "MediaFiles",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_OwnerId_FolderId_OriginalNameLower",
                schema: "keepr",
                table: "MediaFiles",
                columns: new[] { "OwnerId", "FolderId", "OriginalNameLower" },
                unique: true,
                filter: "\"Status\" <> 'Failed' AND \"DeletedAt\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_OwnerId_FolderId_Status",
                schema: "keepr",
                table: "MediaFiles",
                columns: new[] { "OwnerId", "FolderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Folders_DeletedAt",
                schema: "keepr",
                table: "Folders",
                column: "DeletedAt",
                filter: "\"DeletedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_OwnerId_ParentId",
                schema: "keepr",
                table: "Folders",
                columns: new[] { "OwnerId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Folders_OwnerId_ParentId_NameLower",
                schema: "keepr",
                table: "Folders",
                columns: new[] { "OwnerId", "ParentId", "NameLower" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ParentId",
                schema: "keepr",
                table: "Folders",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaFiles_Folders_FolderId",
                schema: "keepr",
                table: "MediaFiles",
                column: "FolderId",
                principalSchema: "keepr",
                principalTable: "Folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaFiles_Folders_FolderId",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.DropTable(
                name: "Folders",
                schema: "keepr");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_DeletedAt",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_FolderId",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_OwnerId_FolderId_OriginalNameLower",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_OwnerId_FolderId_Status",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "DeletedRootId",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "FolderId",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "OriginalNameLower",
                schema: "keepr",
                table: "MediaFiles");

            migrationBuilder.AlterColumn<string>(
                name: "OriginalName",
                schema: "keepr",
                table: "MediaFiles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);
        }
    }
}
