using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace CCTavern.Migrations
{
    /// <inheritdoc />
    public partial class CreateArchivedMessagesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchivedMessages",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    MessageId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    DateMessageCreated = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AuthorId = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ContainsPrefix = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MessageContents = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedMessages_AuthorId",
                table: "ArchivedMessages",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedMessages_GuildId",
                table: "ArchivedMessages",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchivedMessages");
        }
    }
}
