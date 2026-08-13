using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace CCTavern.Migrations.MariaDB
{
    /// <inheritdoc />
    public partial class AddGuildEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuildEvents",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ActorUserId = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    ActorUsername = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ActorDisplayName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    Kind = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    QueueItemId = table.Column<ulong>(type: "bigint unsigned", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PayloadVersion = table.Column<ushort>(type: "smallint unsigned", nullable: false, defaultValue: (ushort)1),
                    PayloadJson = table.Column<string>(type: "json", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildEvents", x => x.Id);
                    table.CheckConstraint("CK_GuildEvents_PayloadJson", "JSON_VALID(`PayloadJson`)");
                    table.ForeignKey(
                        name: "FK_GuildEvents_GuildQueueItems_QueueItemId",
                        column: x => x.QueueItemId,
                        principalTable: "GuildQueueItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuildEvents_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuildEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GuildEvents_ActorUserId_OccurredAtUtc_Id",
                table: "GuildEvents",
                columns: new[] { "ActorUserId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GuildEvents_CorrelationId",
                table: "GuildEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildEvents_GuildId_Kind_OccurredAtUtc_Id",
                table: "GuildEvents",
                columns: new[] { "GuildId", "Kind", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GuildEvents_GuildId_OccurredAtUtc_Id",
                table: "GuildEvents",
                columns: new[] { "GuildId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GuildEvents_QueueItemId",
                table: "GuildEvents",
                column: "QueueItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildEvents");
        }
    }
}
