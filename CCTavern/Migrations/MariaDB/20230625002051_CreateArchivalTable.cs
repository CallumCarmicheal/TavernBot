using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace CCTavern.Migrations
{
    /// <inheritdoc />
    public partial class CreateArchivalTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchivedTracks",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    DateMessageCreated = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Position = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Title = table.Column<string>(type: "longtext", nullable: false),
                    Length = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    TrackString = table.Column<string>(type: "longtext", nullable: false),
                    RequestedById = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedTracks_GuildId",
                table: "ArchivedTracks",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedTracks_RequestedById",
                table: "ArchivedTracks",
                column: "RequestedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchivedTracks");
        }
    }
}
