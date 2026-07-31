using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTavern.Migrations.MariaDB
{
    /// <inheritdoc />
    public partial class EnforceQueueAndTrackUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GuildQueueItems_GuildId",
                table: "GuildQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_CachedUsers_GuildId",
                table: "CachedUsers");

            migrationBuilder.AlterColumn<string>(
                name: "TrackSourceId",
                table: "TrackPlaylistPositions",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AlterColumn<string>(
                name: "TrackSource",
                table: "TrackPlaylistPositions",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.CreateIndex(
                name: "UX_TrackPlaylistPositions_Source_SourceId_Position",
                table: "TrackPlaylistPositions",
                columns: new[] { "TrackSource", "TrackSourceId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_GuildId_Position",
                table: "GuildQueueItems",
                columns: new[] { "GuildId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CachedUsers_GuildId_UserId",
                table: "CachedUsers",
                columns: new[] { "GuildId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TrackPlaylistPositions_Source_SourceId_Position",
                table: "TrackPlaylistPositions");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueueItems_GuildId_Position",
                table: "GuildQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_CachedUsers_GuildId_UserId",
                table: "CachedUsers");

            migrationBuilder.AlterColumn<string>(
                name: "TrackSourceId",
                table: "TrackPlaylistPositions",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)");

            migrationBuilder.AlterColumn<string>(
                name: "TrackSource",
                table: "TrackPlaylistPositions",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_GuildId",
                table: "GuildQueueItems",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_CachedUsers_GuildId",
                table: "CachedUsers",
                column: "GuildId");
        }
    }
}
