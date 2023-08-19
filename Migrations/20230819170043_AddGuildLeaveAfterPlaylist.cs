using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTavern.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildLeaveAfterPlaylist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LeaveAfterPlaylist",
                table: "Guilds",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaveAfterPlaylist",
                table: "Guilds");
        }
    }
}
