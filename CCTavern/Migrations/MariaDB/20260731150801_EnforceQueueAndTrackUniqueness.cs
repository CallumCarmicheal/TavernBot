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
            // Fail before permanent DDL if any data would violate the new unique indexes.
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE `__QueueAndTrackUniquenessPreflight` (
                    `Id` int NOT NULL PRIMARY KEY
                );
                """);
            migrationBuilder.Sql(
                "INSERT INTO `__QueueAndTrackUniquenessPreflight` (`Id`) VALUES (1);");
            migrationBuilder.Sql(
                """
                INSERT INTO `__QueueAndTrackUniquenessPreflight` (`Id`)
                SELECT 1
                WHERE EXISTS (
                    SELECT 1
                    FROM `GuildQueueItems`
                    GROUP BY `GuildId`, `Position`
                    HAVING COUNT(*) > 1
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `TrackPlaylistPositions`
                    GROUP BY `TrackSource`, `TrackSourceId`, `Position`
                    HAVING COUNT(*) > 1
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `CachedUsers`
                    GROUP BY `GuildId`, `UserId`
                    HAVING COUNT(*) > 1
                );
                """);
            migrationBuilder.Sql("DROP TEMPORARY TABLE `__QueueAndTrackUniquenessPreflight`;");

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

            // Create the replacements first. GuildQueueItems.GuildId needs an index
            // at all times because it participates in a foreign key.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS
                    `UX_TrackPlaylistPositions_Source_SourceId_Position`
                    ON `TrackPlaylistPositions` (`TrackSource`, `TrackSourceId`, `Position`);
                """);
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS
                    `IX_GuildQueueItems_GuildId_Position`
                    ON `GuildQueueItems` (`GuildId`, `Position`);
                """);
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS
                    `IX_CachedUsers_GuildId_UserId`
                    ON `CachedUsers` (`GuildId`, `UserId`);
                """);

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS `IX_GuildQueueItems_GuildId` ON `GuildQueueItems`;");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS `IX_CachedUsers_GuildId` ON `CachedUsers`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the foreign-key-supporting indexes before removing their replacements.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS `IX_GuildQueueItems_GuildId` ON `GuildQueueItems` (`GuildId`);");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS `IX_CachedUsers_GuildId` ON `CachedUsers` (`GuildId`);");

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS `UX_TrackPlaylistPositions_Source_SourceId_Position`
                    ON `TrackPlaylistPositions`;
                """);
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS `IX_GuildQueueItems_GuildId_Position`
                    ON `GuildQueueItems`;
                """);
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS `IX_CachedUsers_GuildId_UserId` ON `CachedUsers`;");

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

        }
    }
}
