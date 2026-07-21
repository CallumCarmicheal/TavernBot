using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTavern.Migrations
{
    /// <inheritdoc />
    public partial class CreateInitialDatabaseMsSQL : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Guilds",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Prefixes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MusicChannelId = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    MusicChannelName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastMessageStatusId = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    NextTrack = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    TrackCount = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    CurrentTrack = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    IsPlaying = table.Column<bool>(type: "bit", nullable: false),
                    LeaveAfterQueue = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guilds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CachedUsers",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GuildId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CachedUsers_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArchivedMessages",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    DateMessageCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AuthorId = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    GuildId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    ContainsPrefix = table.Column<bool>(type: "bit", nullable: false),
                    MessageContents = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivedMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchivedMessages_CachedUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "CachedUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ArchivedMessages_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArchivedTracks",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    DateMessageCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Position = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Length = table.Column<TimeSpan>(type: "time", nullable: false),
                    TrackString = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedById = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    GuildId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivedTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchivedTracks_CachedUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "CachedUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ArchivedTracks_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuildQueuePlaylists",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlaylistSongCount = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildQueuePlaylists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildQueuePlaylists_CachedUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "CachedUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GuildQueueItems",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GuildId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    Position = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Length = table.Column<TimeSpan>(type: "time", nullable: false),
                    TrackString = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DateDeleted = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedById = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    DeletedReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedById = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    PlaylistId = table.Column<decimal>(type: "decimal(20,0)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildQueueItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildQueueItems_CachedUsers_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "CachedUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GuildQueueItems_CachedUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "CachedUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GuildQueueItems_GuildQueuePlaylists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "GuildQueuePlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GuildQueueItems_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedMessages_AuthorId",
                table: "ArchivedMessages",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedMessages_GuildId",
                table: "ArchivedMessages",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedTracks_GuildId",
                table: "ArchivedTracks",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedTracks_RequestedById",
                table: "ArchivedTracks",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_CachedUsers_GuildId",
                table: "CachedUsers",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_DeletedById",
                table: "GuildQueueItems",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_GuildId",
                table: "GuildQueueItems",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_PlaylistId",
                table: "GuildQueueItems",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_RequestedById",
                table: "GuildQueueItems",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueuePlaylists_CreatedById",
                table: "GuildQueuePlaylists",
                column: "CreatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchivedMessages");

            migrationBuilder.DropTable(
                name: "ArchivedTracks");

            migrationBuilder.DropTable(
                name: "GuildQueueItems");

            migrationBuilder.DropTable(
                name: "GuildQueuePlaylists");

            migrationBuilder.DropTable(
                name: "CachedUsers");

            migrationBuilder.DropTable(
                name: "Guilds");
        }
    }
}
