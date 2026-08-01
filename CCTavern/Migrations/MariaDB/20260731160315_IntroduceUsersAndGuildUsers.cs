using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCTavern.Migrations.MariaDB
{
    /// <inheritdoc />
    public partial class IntroduceUsersAndGuildUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Run checks that only depend on the legacy schema before issuing
            // any DDL. MariaDB DDL is not fully transactional, so failing here
            // avoids leaving a partially expanded schema.
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE `__UserMigrationPreflight` (
                    `Id` INT NOT NULL PRIMARY KEY
                );

                INSERT INTO `__UserMigrationPreflight` (`Id`) VALUES (1);

                INSERT INTO `__UserMigrationPreflight` (`Id`)
                SELECT 1
                WHERE EXISTS (
                    SELECT 1
                    FROM `GuildQueueItems` AS queueItem
                    LEFT JOIN `CachedUsers` AS requestedBy
                        ON requestedBy.`Id` = queueItem.`RequestedById`
                    LEFT JOIN `CachedUsers` AS deletedBy
                        ON deletedBy.`Id` = queueItem.`DeletedById`
                    WHERE (queueItem.`RequestedById` IS NOT NULL AND requestedBy.`Id` IS NULL)
                       OR (queueItem.`DeletedById` IS NOT NULL AND deletedBy.`Id` IS NULL)
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `ArchivedTracks` AS archivedTrack
                    LEFT JOIN `CachedUsers` AS cachedUser
                        ON cachedUser.`Id` = archivedTrack.`RequestedById`
                    WHERE archivedTrack.`RequestedById` IS NOT NULL
                      AND cachedUser.`Id` IS NULL
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `ArchivedMessages` AS archivedMessage
                    LEFT JOIN `CachedUsers` AS cachedUser
                        ON cachedUser.`Id` = archivedMessage.`AuthorId`
                    WHERE archivedMessage.`AuthorId` IS NOT NULL
                      AND cachedUser.`Id` IS NULL
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `GuildQueuePlaylists` AS playlist
                    LEFT JOIN `CachedUsers` AS cachedUser
                        ON cachedUser.`Id` = playlist.`CreatedById`
                    WHERE playlist.`CreatedById` IS NOT NULL
                      AND cachedUser.`Id` IS NULL
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `GuildQueueItems`
                    WHERE `PlaylistId` IS NOT NULL
                    GROUP BY `PlaylistId`
                    HAVING COUNT(DISTINCT `GuildId`) > 1
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `GuildQueuePlaylists` AS playlist
                    LEFT JOIN `GuildQueueItems` AS queueItem
                        ON queueItem.`PlaylistId` = playlist.`Id`
                    LEFT JOIN `CachedUsers` AS creator
                        ON creator.`Id` = playlist.`CreatedById`
                    GROUP BY playlist.`Id`, creator.`GuildId`
                    HAVING COUNT(queueItem.`Id`) = 0
                       AND creator.`GuildId` IS NULL
                );

                DROP TEMPORARY TABLE `__UserMigrationPreflight`;
                """);

            migrationBuilder.AddColumn<ulong>(
                name: "CreatedByUserId",
                table: "GuildQueuePlaylists",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "GuildId",
                table: "GuildQueuePlaylists",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "DeletedByUserId",
                table: "GuildQueueItems",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "RequestedByUserId",
                table: "GuildQueueItems",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "RequestedByUserId",
                table: "ArchivedTracks",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "AuthorUserId",
                table: "ArchivedMessages",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Username = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GuildUsers",
                columns: table => new
                {
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildUsers", x => new { x.GuildId, x.UserId });
                    table.ForeignKey(
                        name: "FK_GuildUsers_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuildUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.Sql(
                """
                INSERT INTO `Users` (`Id`, `Username`, `CreatedAt`, `UpdatedAt`)
                SELECT
                    ranked.`UserId`,
                    ranked.`Username`,
                    ranked.`FirstSeenAt`,
                    ranked.`LastSeenAt`
                FROM (
                    SELECT
                        cu.`Id`,
                        cu.`UserId`,
                        cu.`Username`,
                        MIN(cu.`CreatedAt`) OVER (PARTITION BY cu.`UserId`) AS `FirstSeenAt`,
                        MAX(COALESCE(cu.`UpdatedAt`, cu.`CreatedAt`)) OVER (PARTITION BY cu.`UserId`) AS `LastSeenAt`,
                        ROW_NUMBER() OVER (
                            PARTITION BY cu.`UserId`
                            ORDER BY COALESCE(cu.`UpdatedAt`, cu.`CreatedAt`) DESC,
                                     cu.`CreatedAt` DESC,
                                     cu.`Id` DESC
                        ) AS `UserRank`
                    FROM `CachedUsers` AS cu
                ) AS ranked
                WHERE ranked.`UserRank` = 1;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO `GuildUsers`
                    (`GuildId`, `UserId`, `DisplayName`, `CreatedAt`, `UpdatedAt`)
                SELECT
                    cu.`GuildId`,
                    cu.`UserId`,
                    cu.`DisplayName`,
                    cu.`CreatedAt`,
                    cu.`UpdatedAt`
                FROM `CachedUsers` AS cu;
                """);

            migrationBuilder.Sql(
                """
                UPDATE `GuildQueueItems` AS queueItem
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`Id` = queueItem.`RequestedById`
                SET queueItem.`RequestedByUserId` = cachedUser.`UserId`
                WHERE queueItem.`RequestedById` IS NOT NULL;

                UPDATE `GuildQueueItems` AS queueItem
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`Id` = queueItem.`DeletedById`
                SET queueItem.`DeletedByUserId` = cachedUser.`UserId`
                WHERE queueItem.`DeletedById` IS NOT NULL;

                UPDATE `ArchivedTracks` AS archivedTrack
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`Id` = archivedTrack.`RequestedById`
                SET archivedTrack.`RequestedByUserId` = cachedUser.`UserId`
                WHERE archivedTrack.`RequestedById` IS NOT NULL;

                UPDATE `ArchivedMessages` AS archivedMessage
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`Id` = archivedMessage.`AuthorId`
                SET archivedMessage.`AuthorUserId` = cachedUser.`UserId`
                WHERE archivedMessage.`AuthorId` IS NOT NULL;

                UPDATE `GuildQueuePlaylists` AS playlist
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`Id` = playlist.`CreatedById`
                SET playlist.`CreatedByUserId` = cachedUser.`UserId`
                WHERE playlist.`CreatedById` IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE `GuildQueuePlaylists` AS playlist
                INNER JOIN (
                    SELECT
                        queueItem.`PlaylistId`,
                        MIN(queueItem.`GuildId`) AS `GuildId`
                    FROM `GuildQueueItems` AS queueItem
                    WHERE queueItem.`PlaylistId` IS NOT NULL
                    GROUP BY queueItem.`PlaylistId`
                    HAVING COUNT(DISTINCT queueItem.`GuildId`) = 1
                ) AS inferredGuild
                    ON inferredGuild.`PlaylistId` = playlist.`Id`
                SET playlist.`GuildId` = inferredGuild.`GuildId`;

                UPDATE `GuildQueuePlaylists` AS playlist
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`Id` = playlist.`CreatedById`
                SET playlist.`GuildId` = cachedUser.`GuildId`
                WHERE playlist.`GuildId` IS NULL;
                """);

            // Every validation INSERT must return no rows. A returned row collides
            // with the seeded primary key and aborts the migration.
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE `__UserMigrationValidation` (
                    `Id` INT NOT NULL PRIMARY KEY
                );

                INSERT INTO `__UserMigrationValidation` (`Id`) VALUES (1);

                INSERT INTO `__UserMigrationValidation` (`Id`)
                SELECT 1
                WHERE EXISTS (
                    SELECT 1
                    FROM `GuildQueueItems`
                    WHERE (`RequestedById` IS NOT NULL AND `RequestedByUserId` IS NULL)
                       OR (`DeletedById` IS NOT NULL AND `DeletedByUserId` IS NULL)
                )
                   OR EXISTS (
                    SELECT 1 FROM `ArchivedTracks`
                    WHERE `RequestedById` IS NOT NULL AND `RequestedByUserId` IS NULL
                )
                   OR EXISTS (
                    SELECT 1 FROM `ArchivedMessages`
                    WHERE `AuthorId` IS NOT NULL AND `AuthorUserId` IS NULL
                )
                   OR EXISTS (
                    SELECT 1 FROM `GuildQueuePlaylists`
                    WHERE (`CreatedById` IS NOT NULL AND `CreatedByUserId` IS NULL)
                       OR `GuildId` IS NULL
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `GuildQueueItems`
                    WHERE `PlaylistId` IS NOT NULL
                    GROUP BY `PlaylistId`
                    HAVING COUNT(DISTINCT `GuildId`) > 1
                );

                DROP TEMPORARY TABLE `__UserMigrationValidation`;
                """);

            migrationBuilder.AlterColumn<ulong>(
                name: "GuildId",
                table: "GuildQueuePlaylists",
                type: "bigint unsigned",
                nullable: false,
                oldClrType: typeof(ulong),
                oldType: "bigint unsigned",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueuePlaylists_CreatedByUserId",
                table: "GuildQueuePlaylists",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueuePlaylists_GuildId",
                table: "GuildQueuePlaylists",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_DeletedByUserId",
                table: "GuildQueueItems",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_RequestedByUserId",
                table: "GuildQueueItems",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedTracks_RequestedByUserId",
                table: "ArchivedTracks",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedMessages_AuthorUserId",
                table: "ArchivedMessages",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildUsers_UserId",
                table: "GuildUsers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ArchivedMessages_Users_AuthorUserId",
                table: "ArchivedMessages",
                column: "AuthorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ArchivedTracks_Users_RequestedByUserId",
                table: "ArchivedTracks",
                column: "RequestedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildQueueItems_Users_DeletedByUserId",
                table: "GuildQueueItems",
                column: "DeletedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildQueueItems_Users_RequestedByUserId",
                table: "GuildQueueItems",
                column: "RequestedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildQueuePlaylists_Guilds_GuildId",
                table: "GuildQueuePlaylists",
                column: "GuildId",
                principalTable: "Guilds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildQueuePlaylists_Users_CreatedByUserId",
                table: "GuildQueuePlaylists",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArchivedMessages_Users_AuthorUserId",
                table: "ArchivedMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ArchivedTracks_Users_RequestedByUserId",
                table: "ArchivedTracks");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildQueueItems_Users_DeletedByUserId",
                table: "GuildQueueItems");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildQueueItems_Users_RequestedByUserId",
                table: "GuildQueueItems");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildQueuePlaylists_Guilds_GuildId",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildQueuePlaylists_Users_CreatedByUserId",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropTable(
                name: "GuildUsers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueuePlaylists_CreatedByUserId",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueuePlaylists_GuildId",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueueItems_DeletedByUserId",
                table: "GuildQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueueItems_RequestedByUserId",
                table: "GuildQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_ArchivedTracks_RequestedByUserId",
                table: "ArchivedTracks");

            migrationBuilder.DropIndex(
                name: "IX_ArchivedMessages_AuthorUserId",
                table: "ArchivedMessages");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "GuildQueueItems");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "GuildQueueItems");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "ArchivedTracks");

            migrationBuilder.DropColumn(
                name: "AuthorUserId",
                table: "ArchivedMessages");
        }
    }
}
