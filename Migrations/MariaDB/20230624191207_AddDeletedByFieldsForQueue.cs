using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTavern.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedByFieldsForQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateDeleted",
                table: "GuildQueueItems",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<ulong>(
                name: "DeletedById",
                table: "GuildQueueItems",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_DeletedById",
                table: "GuildQueueItems",
                column: "DeletedById");

            migrationBuilder.AddForeignKey(
                name: "FK_GuildQueueItems_CachedUsers_DeletedById",
                table: "GuildQueueItems",
                column: "DeletedById",
                principalTable: "CachedUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GuildQueueItems_CachedUsers_DeletedById",
                table: "GuildQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueueItems_DeletedById",
                table: "GuildQueueItems");

            migrationBuilder.DropColumn(
                name: "DateDeleted",
                table: "GuildQueueItems");

            migrationBuilder.DropColumn(
                name: "DeletedById",
                table: "GuildQueueItems");
        }
    }
}
