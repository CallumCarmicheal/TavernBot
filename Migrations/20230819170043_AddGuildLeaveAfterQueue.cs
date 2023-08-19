using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTavern.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildLeaveAfterQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LeaveAfterQueue",
                table: "Guilds",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaveAfterQueue",
                table: "Guilds");
        }
    }
}
