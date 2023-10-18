using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace CCTavern.Migrations
{
    /// <inheritdoc />
    public partial class InitialDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Guilds",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    Prefixes = table.Column<string>(type: "longtext", nullable: true),
                    MusicChannelId = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    MusicChannelName = table.Column<string>(type: "longtext", nullable: true),
                    LastMessageStatusId = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    NextTrack = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    TrackCount = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CurrentTrack = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    IsPlaying = table.Column<bool>(type: "tinyint(1)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guilds", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CachedUsers",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Username = table.Column<string>(type: "longtext", nullable: false),
                    DisplayName = table.Column<string>(type: "longtext", nullable: false),
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GuildQueuePlaylists",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Title = table.Column<string>(type: "longtext", nullable: false),
                    PlaylistSongCount = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildQueuePlaylists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildQueuePlaylists_CachedUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "CachedUsers",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GuildQueueItems",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Position = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Title = table.Column<string>(type: "longtext", nullable: false),
                    Length = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    TrackString = table.Column<string>(type: "longtext", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequestedById = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    PlaylistId = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildQueueItems", x => x.Id);
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CachedUsers_GuildId",
                table: "CachedUsers",
                column: "GuildId");

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
