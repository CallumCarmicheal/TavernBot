using Microsoft.EntityFrameworkCore.Migrations;

using MySql.EntityFrameworkCore.Metadata;

using System;

#nullable disable

namespace CCTavern.Migrations.MariaDB {
    /// <inheritdoc />
    public partial class RemoveCachedUsers : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            // Abort before removing the legacy schema if any populated legacy
            // actor does not agree with its replacement Discord user ID.
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE `__CachedUserRemovalValidation` (
                    `Id` INT NOT NULL PRIMARY KEY
                );

                INSERT INTO `__CachedUserRemovalValidation` (`Id`) VALUES (1);

                INSERT INTO `__CachedUserRemovalValidation` (`Id`)
                SELECT 1
                WHERE EXISTS (
                    SELECT 1
                    FROM `GuildQueueItems` AS queueItem
                    LEFT JOIN `CachedUsers` AS cachedUser
                        ON cachedUser.`Id` = queueItem.`RequestedById`
                    WHERE queueItem.`RequestedById` IS NOT NULL
                      AND (cachedUser.`Id` IS NULL
                           OR queueItem.`RequestedByUserId` IS NULL
                           OR queueItem.`RequestedByUserId` <> cachedUser.`UserId`)
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `GuildQueueItems` AS queueItem
                    LEFT JOIN `CachedUsers` AS cachedUser
                        ON cachedUser.`Id` = queueItem.`DeletedById`
                    WHERE queueItem.`DeletedById` IS NOT NULL
                      AND (cachedUser.`Id` IS NULL
                           OR queueItem.`DeletedByUserId` IS NULL
                           OR queueItem.`DeletedByUserId` <> cachedUser.`UserId`)
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `ArchivedTracks` AS archivedTrack
                    LEFT JOIN `CachedUsers` AS cachedUser
                        ON cachedUser.`Id` = archivedTrack.`RequestedById`
                    WHERE archivedTrack.`RequestedById` IS NOT NULL
                      AND (cachedUser.`Id` IS NULL
                           OR archivedTrack.`RequestedByUserId` IS NULL
                           OR archivedTrack.`RequestedByUserId` <> cachedUser.`UserId`)
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `ArchivedMessages` AS archivedMessage
                    LEFT JOIN `CachedUsers` AS cachedUser
                        ON cachedUser.`Id` = archivedMessage.`AuthorId`
                    WHERE archivedMessage.`AuthorId` IS NOT NULL
                      AND (cachedUser.`Id` IS NULL
                           OR archivedMessage.`AuthorUserId` IS NULL
                           OR archivedMessage.`AuthorUserId` <> cachedUser.`UserId`)
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `GuildQueuePlaylists` AS playlist
                    LEFT JOIN `CachedUsers` AS cachedUser
                        ON cachedUser.`Id` = playlist.`CreatedById`
                    WHERE playlist.`CreatedById` IS NOT NULL
                      AND (cachedUser.`Id` IS NULL
                           OR playlist.`CreatedByUserId` IS NULL
                           OR playlist.`CreatedByUserId` <> cachedUser.`UserId`)
                );

                DROP TEMPORARY TABLE `__CachedUserRemovalValidation`;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ArchivedMessages_CachedUsers_AuthorId",
                table: "ArchivedMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ArchivedTracks_CachedUsers_RequestedById",
                table: "ArchivedTracks");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildQueueItems_CachedUsers_DeletedById",
                table: "GuildQueueItems");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildQueueItems_CachedUsers_RequestedById",
                table: "GuildQueueItems");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildQueuePlaylists_CachedUsers_CreatedById",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropTable(
                name: "CachedUsers");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueuePlaylists_CreatedById",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueueItems_DeletedById",
                table: "GuildQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_GuildQueueItems_RequestedById",
                table: "GuildQueueItems");

            migrationBuilder.DropIndex(
                name: "IX_ArchivedTracks_RequestedById",
                table: "ArchivedTracks");

            migrationBuilder.DropIndex(
                name: "IX_ArchivedMessages_AuthorId",
                table: "ArchivedMessages");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "GuildQueuePlaylists");

            migrationBuilder.DropColumn(
                name: "DeletedById",
                table: "GuildQueueItems");

            migrationBuilder.DropColumn(
                name: "RequestedById",
                table: "GuildQueueItems");

            migrationBuilder.DropColumn(
                name: "RequestedById",
                table: "ArchivedTracks");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "ArchivedMessages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            // The legacy schema can only represent an actor through a guild
            // profile. Refuse the rollback before DDL if one is missing.
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE `__CachedUserRestorePreflight` (
                    `Id` INT NOT NULL PRIMARY KEY
                );

                INSERT INTO `__CachedUserRestorePreflight` (`Id`) VALUES (1);

                INSERT INTO `__CachedUserRestorePreflight` (`Id`)
                SELECT 1
                WHERE EXISTS (
                    SELECT 1
                    FROM `GuildQueueItems` AS queueItem
                    LEFT JOIN `GuildUsers` AS requestedBy
                        ON requestedBy.`GuildId` = queueItem.`GuildId`
                       AND requestedBy.`UserId` = queueItem.`RequestedByUserId`
                    LEFT JOIN `GuildUsers` AS deletedBy
                        ON deletedBy.`GuildId` = queueItem.`GuildId`
                       AND deletedBy.`UserId` = queueItem.`DeletedByUserId`
                    WHERE (queueItem.`RequestedByUserId` IS NOT NULL AND requestedBy.`UserId` IS NULL)
                       OR (queueItem.`DeletedByUserId` IS NOT NULL AND deletedBy.`UserId` IS NULL)
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `ArchivedTracks` AS archivedTrack
                    LEFT JOIN `GuildUsers` AS guildUser
                        ON guildUser.`GuildId` = archivedTrack.`GuildId`
                       AND guildUser.`UserId` = archivedTrack.`RequestedByUserId`
                    WHERE archivedTrack.`RequestedByUserId` IS NOT NULL
                      AND guildUser.`UserId` IS NULL
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `ArchivedMessages` AS archivedMessage
                    LEFT JOIN `GuildUsers` AS guildUser
                        ON guildUser.`GuildId` = archivedMessage.`GuildId`
                       AND guildUser.`UserId` = archivedMessage.`AuthorUserId`
                    WHERE archivedMessage.`AuthorUserId` IS NOT NULL
                      AND guildUser.`UserId` IS NULL
                )
                   OR EXISTS (
                    SELECT 1
                    FROM `GuildQueuePlaylists` AS playlist
                    LEFT JOIN `GuildUsers` AS guildUser
                        ON guildUser.`GuildId` = playlist.`GuildId`
                       AND guildUser.`UserId` = playlist.`CreatedByUserId`
                    WHERE playlist.`CreatedByUserId` IS NOT NULL
                      AND guildUser.`UserId` IS NULL
                );

                DROP TEMPORARY TABLE `__CachedUserRestorePreflight`;
                """);

            migrationBuilder.AddColumn<ulong>(
                name: "CreatedById",
                table: "GuildQueuePlaylists",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "DeletedById",
                table: "GuildQueueItems",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "RequestedById",
                table: "GuildQueueItems",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "RequestedById",
                table: "ArchivedTracks",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "AuthorId",
                table: "ArchivedMessages",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CachedUsers",
                columns: table => new {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DisplayName = table.Column<string>(type: "longtext", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Username = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_CachedUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CachedUsers_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.Sql(
                """
                INSERT INTO `CachedUsers`
                    (`GuildId`, `UserId`, `Username`, `DisplayName`, `CreatedAt`, `UpdatedAt`)
                SELECT
                    guildUser.`GuildId`,
                    guildUser.`UserId`,
                    user.`Username`,
                    guildUser.`DisplayName`,
                    guildUser.`CreatedAt`,
                    guildUser.`UpdatedAt`
                FROM `GuildUsers` AS guildUser
                INNER JOIN `Users` AS user
                    ON user.`Id` = guildUser.`UserId`;

                UPDATE `GuildQueueItems` AS queueItem
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`GuildId` = queueItem.`GuildId`
                   AND cachedUser.`UserId` = queueItem.`RequestedByUserId`
                SET queueItem.`RequestedById` = cachedUser.`Id`
                WHERE queueItem.`RequestedByUserId` IS NOT NULL;

                UPDATE `GuildQueueItems` AS queueItem
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`GuildId` = queueItem.`GuildId`
                   AND cachedUser.`UserId` = queueItem.`DeletedByUserId`
                SET queueItem.`DeletedById` = cachedUser.`Id`
                WHERE queueItem.`DeletedByUserId` IS NOT NULL;

                UPDATE `ArchivedTracks` AS archivedTrack
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`GuildId` = archivedTrack.`GuildId`
                   AND cachedUser.`UserId` = archivedTrack.`RequestedByUserId`
                SET archivedTrack.`RequestedById` = cachedUser.`Id`
                WHERE archivedTrack.`RequestedByUserId` IS NOT NULL;

                UPDATE `ArchivedMessages` AS archivedMessage
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`GuildId` = archivedMessage.`GuildId`
                   AND cachedUser.`UserId` = archivedMessage.`AuthorUserId`
                SET archivedMessage.`AuthorId` = cachedUser.`Id`
                WHERE archivedMessage.`AuthorUserId` IS NOT NULL;

                UPDATE `GuildQueuePlaylists` AS playlist
                INNER JOIN `CachedUsers` AS cachedUser
                    ON cachedUser.`GuildId` = playlist.`GuildId`
                   AND cachedUser.`UserId` = playlist.`CreatedByUserId`
                SET playlist.`CreatedById` = cachedUser.`Id`
                WHERE playlist.`CreatedByUserId` IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueuePlaylists_CreatedById",
                table: "GuildQueuePlaylists",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_DeletedById",
                table: "GuildQueueItems",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_GuildQueueItems_RequestedById",
                table: "GuildQueueItems",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedTracks_RequestedById",
                table: "ArchivedTracks",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedMessages_AuthorId",
                table: "ArchivedMessages",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_CachedUsers_GuildId_UserId",
                table: "CachedUsers",
                columns: new[] { "GuildId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ArchivedMessages_CachedUsers_AuthorId",
                table: "ArchivedMessages",
                column: "AuthorId",
                principalTable: "CachedUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ArchivedTracks_CachedUsers_RequestedById",
                table: "ArchivedTracks",
                column: "RequestedById",
                principalTable: "CachedUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GuildQueueItems_CachedUsers_DeletedById",
                table: "GuildQueueItems",
                column: "DeletedById",
                principalTable: "CachedUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildQueueItems_CachedUsers_RequestedById",
                table: "GuildQueueItems",
                column: "RequestedById",
                principalTable: "CachedUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildQueuePlaylists_CachedUsers_CreatedById",
                table: "GuildQueuePlaylists",
                column: "CreatedById",
                principalTable: "CachedUsers",
                principalColumn: "Id");
        }
    }
}