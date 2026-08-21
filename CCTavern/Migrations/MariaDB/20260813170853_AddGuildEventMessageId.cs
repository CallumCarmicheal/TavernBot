using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTavern.Migrations.MariaDB {
    /// <inheritdoc />
    public partial class AddGuildEventMessageId : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddColumn<ulong>(
                name: "MessageId",
                table: "GuildEvents",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildEvents_MessageId",
                table: "GuildEvents",
                column: "MessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropIndex(
                name: "IX_GuildEvents_MessageId",
                table: "GuildEvents");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "GuildEvents");
        }
    }
}