-- Post-upgrade audit for the expand stage of the Users/GuildUsers migration.
-- Expected state:
--   * EnforceQueueAndTrackUniqueness and IntroduceUsersAndGuildUsers are applied.
--   * RemoveCachedUsers is still pending.
--   * CachedUsers and the legacy actor columns still exist for comparison.
--
-- This script only creates a session-scoped temporary table. It does not
-- modify persistent application data or schema.

DROP TEMPORARY TABLE IF EXISTS `__PostUpgradeChecks`;

CREATE TEMPORARY TABLE `__PostUpgradeChecks` (
    `CheckOrder` int NOT NULL,
    `CheckGroup` varchar(64) NOT NULL,
    `CheckName` varchar(160) NOT NULL,
    `ExpectedValue` bigint NOT NULL,
    `ActualValue` bigint NOT NULL,
    `Passed` tinyint(1) NOT NULL,
    PRIMARY KEY (`CheckOrder`)
);

-- Migration stage -----------------------------------------------------------

INSERT INTO `__PostUpgradeChecks`
SELECT 10, 'Migration history', 'Queue/track uniqueness migration applied',
       1, COUNT(*), COUNT(*) = 1
FROM `__EFMigrationsHistory`
WHERE `MigrationId` = '20260731150801_EnforceQueueAndTrackUniqueness';

INSERT INTO `__PostUpgradeChecks`
SELECT 11, 'Migration history', 'Users/GuildUsers expand migration applied',
       1, COUNT(*), COUNT(*) = 1
FROM `__EFMigrationsHistory`
WHERE `MigrationId` = '20260731160315_IntroduceUsersAndGuildUsers';

INSERT INTO `__PostUpgradeChecks`
SELECT 12, 'Migration history', 'Destructive CachedUsers removal remains pending',
       0, COUNT(*), COUNT(*) = 0
FROM `__EFMigrationsHistory`
WHERE `MigrationId` = '20260731161550_RemoveCachedUsers';

-- Expand-stage schema -------------------------------------------------------

INSERT INTO `__PostUpgradeChecks`
SELECT 20, 'Schema', 'Users and GuildUsers tables exist',
       2, COUNT(*), COUNT(*) = 2
FROM `information_schema`.`TABLES`
WHERE `TABLE_SCHEMA` = DATABASE()
  AND `TABLE_NAME` IN ('Users', 'GuildUsers');

INSERT INTO `__PostUpgradeChecks`
SELECT 21, 'Schema', 'CachedUsers remains available during expand stage',
       1, COUNT(*), COUNT(*) = 1
FROM `information_schema`.`TABLES`
WHERE `TABLE_SCHEMA` = DATABASE()
  AND `TABLE_NAME` = 'CachedUsers';

INSERT INTO `__PostUpgradeChecks`
SELECT 22, 'Schema', 'All replacement actor and playlist columns exist',
       6, COUNT(*), COUNT(*) = 6
FROM `information_schema`.`COLUMNS`
WHERE `TABLE_SCHEMA` = DATABASE()
  AND (`TABLE_NAME`, `COLUMN_NAME`) IN (
      ('GuildQueueItems', 'RequestedByUserId'),
      ('GuildQueueItems', 'DeletedByUserId'),
      ('ArchivedTracks', 'RequestedByUserId'),
      ('ArchivedMessages', 'AuthorUserId'),
      ('GuildQueuePlaylists', 'CreatedByUserId'),
      ('GuildQueuePlaylists', 'GuildId')
  );

INSERT INTO `__PostUpgradeChecks`
SELECT 23, 'Schema', 'Legacy actor columns remain during expand stage',
       5, COUNT(*), COUNT(*) = 5
FROM `information_schema`.`COLUMNS`
WHERE `TABLE_SCHEMA` = DATABASE()
  AND (`TABLE_NAME`, `COLUMN_NAME`) IN (
      ('GuildQueueItems', 'RequestedById'),
      ('GuildQueueItems', 'DeletedById'),
      ('ArchivedTracks', 'RequestedById'),
      ('ArchivedMessages', 'AuthorId'),
      ('GuildQueuePlaylists', 'CreatedById')
  );

INSERT INTO `__PostUpgradeChecks`
SELECT 24, 'Schema', 'Playlist GuildId is required',
       1, COUNT(*), COUNT(*) = 1
FROM `information_schema`.`COLUMNS`
WHERE `TABLE_SCHEMA` = DATABASE()
  AND `TABLE_NAME` = 'GuildQueuePlaylists'
  AND `COLUMN_NAME` = 'GuildId'
  AND `IS_NULLABLE` = 'NO';

INSERT INTO `__PostUpgradeChecks`
SELECT 25, 'Schema', 'Users.Id is an unsigned non-generated bigint',
       1, COUNT(*), COUNT(*) = 1
FROM `information_schema`.`COLUMNS`
WHERE `TABLE_SCHEMA` = DATABASE()
  AND `TABLE_NAME` = 'Users'
  AND `COLUMN_NAME` = 'Id'
  AND `DATA_TYPE` = 'bigint'
  AND `COLUMN_TYPE` LIKE '%unsigned%'
  AND `EXTRA` NOT LIKE '%auto_increment%';

INSERT INTO `__PostUpgradeChecks`
SELECT 26, 'Schema', 'All new foreign keys exist',
       8, COUNT(DISTINCT `CONSTRAINT_NAME`), COUNT(DISTINCT `CONSTRAINT_NAME`) = 8
FROM `information_schema`.`REFERENTIAL_CONSTRAINTS`
WHERE `CONSTRAINT_SCHEMA` = DATABASE()
  AND `CONSTRAINT_NAME` IN (
      'FK_GuildUsers_Guilds_GuildId',
      'FK_GuildUsers_Users_UserId',
      'FK_ArchivedMessages_Users_AuthorUserId',
      'FK_ArchivedTracks_Users_RequestedByUserId',
      'FK_GuildQueueItems_Users_DeletedByUserId',
      'FK_GuildQueueItems_Users_RequestedByUserId',
      'FK_GuildQueuePlaylists_Guilds_GuildId',
      'FK_GuildQueuePlaylists_Users_CreatedByUserId'
  );

-- Required uniqueness -------------------------------------------------------

INSERT INTO `__PostUpgradeChecks`
SELECT 30, 'Indexes', 'Unique queue position index exists',
       1, COUNT(*), COUNT(*) = 1
FROM (
    SELECT `INDEX_NAME`
    FROM `information_schema`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'GuildQueueItems'
      AND `INDEX_NAME` = 'IX_GuildQueueItems_GuildId_Position'
    GROUP BY `INDEX_NAME`
    HAVING MIN(`NON_UNIQUE`) = 0
       AND GROUP_CONCAT(`COLUMN_NAME` ORDER BY `SEQ_IN_INDEX` SEPARATOR ',') = 'GuildId,Position'
) AS validIndex;

INSERT INTO `__PostUpgradeChecks`
SELECT 31, 'Indexes', 'Unique track position index exists',
       1, COUNT(*), COUNT(*) = 1
FROM (
    SELECT `INDEX_NAME`
    FROM `information_schema`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'TrackPlaylistPositions'
      AND `INDEX_NAME` = 'UX_TrackPlaylistPositions_Source_SourceId_Position'
    GROUP BY `INDEX_NAME`
    HAVING MIN(`NON_UNIQUE`) = 0
       AND GROUP_CONCAT(`COLUMN_NAME` ORDER BY `SEQ_IN_INDEX` SEPARATOR ',') = 'TrackSource,TrackSourceId,Position'
) AS validIndex;

INSERT INTO `__PostUpgradeChecks`
SELECT 32, 'Indexes', 'Unique cached guild-user index exists',
       1, COUNT(*), COUNT(*) = 1
FROM (
    SELECT `INDEX_NAME`
    FROM `information_schema`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'CachedUsers'
      AND `INDEX_NAME` = 'IX_CachedUsers_GuildId_UserId'
    GROUP BY `INDEX_NAME`
    HAVING MIN(`NON_UNIQUE`) = 0
       AND GROUP_CONCAT(`COLUMN_NAME` ORDER BY `SEQ_IN_INDEX` SEPARATOR ',') = 'GuildId,UserId'
) AS validIndex;

INSERT INTO `__PostUpgradeChecks`
SELECT 33, 'Data uniqueness', 'Duplicate queue positions',
       0, COUNT(*), COUNT(*) = 0
FROM (
    SELECT 1
    FROM `GuildQueueItems`
    GROUP BY `GuildId`, `Position`
    HAVING COUNT(*) > 1
) AS duplicates;

INSERT INTO `__PostUpgradeChecks`
SELECT 34, 'Data uniqueness', 'Duplicate track positions',
       0, COUNT(*), COUNT(*) = 0
FROM (
    SELECT 1
    FROM `TrackPlaylistPositions`
    GROUP BY `TrackSource`, `TrackSourceId`, `Position`
    HAVING COUNT(*) > 1
) AS duplicates;

INSERT INTO `__PostUpgradeChecks`
SELECT 35, 'Data uniqueness', 'Duplicate cached guild users',
       0, COUNT(*), COUNT(*) = 0
FROM (
    SELECT 1
    FROM `CachedUsers`
    GROUP BY `GuildId`, `UserId`
    HAVING COUNT(*) > 1
) AS duplicates;

-- Identity backfill ---------------------------------------------------------

INSERT INTO `__PostUpgradeChecks`
SELECT 40, 'Identity backfill', 'Global Users cover every legacy Discord user',
       COUNT(DISTINCT cachedUser.`UserId`),
       (SELECT COUNT(*) FROM `Users`),
       (SELECT COUNT(*) FROM `Users`) >= COUNT(DISTINCT cachedUser.`UserId`)
FROM `CachedUsers` AS cachedUser;

INSERT INTO `__PostUpgradeChecks`
SELECT 41, 'Identity backfill', 'GuildUsers cover every legacy guild profile',
       COUNT(*),
       (SELECT COUNT(*) FROM `GuildUsers`),
       (SELECT COUNT(*) FROM `GuildUsers`) >= COUNT(*)
FROM `CachedUsers`;

INSERT INTO `__PostUpgradeChecks`
SELECT 42, 'Identity backfill', 'Legacy Discord users missing from Users',
       0, COUNT(*), COUNT(*) = 0
FROM (
    SELECT DISTINCT cachedUser.`UserId`
    FROM `CachedUsers` AS cachedUser
    LEFT JOIN `Users` AS user ON user.`Id` = cachedUser.`UserId`
    WHERE user.`Id` IS NULL
) AS missingUsers;

INSERT INTO `__PostUpgradeChecks`
SELECT 43, 'Identity backfill', 'Legacy profiles missing from GuildUsers',
       0, COUNT(*), COUNT(*) = 0
FROM `CachedUsers` AS cachedUser
LEFT JOIN `GuildUsers` AS guildUser
  ON guildUser.`GuildId` = cachedUser.`GuildId`
 AND guildUser.`UserId` = cachedUser.`UserId`
WHERE guildUser.`UserId` IS NULL;

INSERT INTO `__PostUpgradeChecks`
SELECT 44, 'Identity data', 'Empty global usernames',
       0, COUNT(*), COUNT(*) = 0
FROM `Users`
WHERE `Username` IS NULL OR TRIM(`Username`) = '';

INSERT INTO `__PostUpgradeChecks`
SELECT 45, 'Identity data', 'Empty guild display names',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildUsers`
WHERE `DisplayName` IS NULL OR TRIM(`DisplayName`) = '';

-- Legacy-to-new actor mapping ----------------------------------------------

INSERT INTO `__PostUpgradeChecks`
SELECT 50, 'Actor mapping', 'Invalid queue requester mappings',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildQueueItems` AS queueItem
LEFT JOIN `CachedUsers` AS cachedUser ON cachedUser.`Id` = queueItem.`RequestedById`
WHERE queueItem.`RequestedById` IS NOT NULL
  AND (cachedUser.`Id` IS NULL
       OR queueItem.`RequestedByUserId` IS NULL
       OR queueItem.`RequestedByUserId` <> cachedUser.`UserId`);

INSERT INTO `__PostUpgradeChecks`
SELECT 51, 'Actor mapping', 'Invalid queue deleter mappings',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildQueueItems` AS queueItem
LEFT JOIN `CachedUsers` AS cachedUser ON cachedUser.`Id` = queueItem.`DeletedById`
WHERE queueItem.`DeletedById` IS NOT NULL
  AND (cachedUser.`Id` IS NULL
       OR queueItem.`DeletedByUserId` IS NULL
       OR queueItem.`DeletedByUserId` <> cachedUser.`UserId`);

INSERT INTO `__PostUpgradeChecks`
SELECT 52, 'Actor mapping', 'Invalid archived-track requester mappings',
       0, COUNT(*), COUNT(*) = 0
FROM `ArchivedTracks` AS archivedTrack
LEFT JOIN `CachedUsers` AS cachedUser ON cachedUser.`Id` = archivedTrack.`RequestedById`
WHERE archivedTrack.`RequestedById` IS NOT NULL
  AND (cachedUser.`Id` IS NULL
       OR archivedTrack.`RequestedByUserId` IS NULL
       OR archivedTrack.`RequestedByUserId` <> cachedUser.`UserId`);

INSERT INTO `__PostUpgradeChecks`
SELECT 53, 'Actor mapping', 'Invalid archived-message author mappings',
       0, COUNT(*), COUNT(*) = 0
FROM `ArchivedMessages` AS archivedMessage
LEFT JOIN `CachedUsers` AS cachedUser ON cachedUser.`Id` = archivedMessage.`AuthorId`
WHERE archivedMessage.`AuthorId` IS NOT NULL
  AND (cachedUser.`Id` IS NULL
       OR archivedMessage.`AuthorUserId` IS NULL
       OR archivedMessage.`AuthorUserId` <> cachedUser.`UserId`);

INSERT INTO `__PostUpgradeChecks`
SELECT 54, 'Actor mapping', 'Invalid playlist creator mappings',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildQueuePlaylists` AS playlist
LEFT JOIN `CachedUsers` AS cachedUser ON cachedUser.`Id` = playlist.`CreatedById`
WHERE playlist.`CreatedById` IS NOT NULL
  AND (cachedUser.`Id` IS NULL
       OR playlist.`CreatedByUserId` IS NULL
       OR playlist.`CreatedByUserId` <> cachedUser.`UserId`);

-- Referential integrity ----------------------------------------------------

INSERT INTO `__PostUpgradeChecks`
SELECT 60, 'Referential integrity', 'GuildUsers without a global User',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildUsers` AS guildUser
LEFT JOIN `Users` AS user ON user.`Id` = guildUser.`UserId`
WHERE user.`Id` IS NULL;

INSERT INTO `__PostUpgradeChecks`
SELECT 61, 'Referential integrity', 'GuildUsers without a Guild',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildUsers` AS guildUser
LEFT JOIN `Guilds` AS guild ON guild.`Id` = guildUser.`GuildId`
WHERE guild.`Id` IS NULL;

INSERT INTO `__PostUpgradeChecks`
SELECT 62, 'Referential integrity', 'Queue requester user orphans',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildQueueItems` AS queueItem
LEFT JOIN `Users` AS user ON user.`Id` = queueItem.`RequestedByUserId`
WHERE queueItem.`RequestedByUserId` IS NOT NULL AND user.`Id` IS NULL;

INSERT INTO `__PostUpgradeChecks`
SELECT 63, 'Referential integrity', 'Queue deleter user orphans',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildQueueItems` AS queueItem
LEFT JOIN `Users` AS user ON user.`Id` = queueItem.`DeletedByUserId`
WHERE queueItem.`DeletedByUserId` IS NOT NULL AND user.`Id` IS NULL;

INSERT INTO `__PostUpgradeChecks`
SELECT 64, 'Referential integrity', 'Archived-track user orphans',
       0, COUNT(*), COUNT(*) = 0
FROM `ArchivedTracks` AS archivedTrack
LEFT JOIN `Users` AS user ON user.`Id` = archivedTrack.`RequestedByUserId`
WHERE archivedTrack.`RequestedByUserId` IS NOT NULL AND user.`Id` IS NULL;

INSERT INTO `__PostUpgradeChecks`
SELECT 65, 'Referential integrity', 'Archived-message user orphans',
       0, COUNT(*), COUNT(*) = 0
FROM `ArchivedMessages` AS archivedMessage
LEFT JOIN `Users` AS user ON user.`Id` = archivedMessage.`AuthorUserId`
WHERE archivedMessage.`AuthorUserId` IS NOT NULL AND user.`Id` IS NULL;

INSERT INTO `__PostUpgradeChecks`
SELECT 66, 'Referential integrity', 'Playlist creator user orphans',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildQueuePlaylists` AS playlist
LEFT JOIN `Users` AS user ON user.`Id` = playlist.`CreatedByUserId`
WHERE playlist.`CreatedByUserId` IS NOT NULL AND user.`Id` IS NULL;

-- Playlist ownership -------------------------------------------------------

INSERT INTO `__PostUpgradeChecks`
SELECT 70, 'Playlist ownership', 'Playlists without a guild',
       0, COUNT(*), COUNT(*) = 0
FROM `GuildQueuePlaylists`
WHERE `GuildId` IS NULL;

INSERT INTO `__PostUpgradeChecks`
SELECT 71, 'Playlist ownership', 'Playlists referenced by multiple guilds',
       0, COUNT(*), COUNT(*) = 0
FROM (
    SELECT queueItem.`PlaylistId`
    FROM `GuildQueueItems` AS queueItem
    WHERE queueItem.`PlaylistId` IS NOT NULL
    GROUP BY queueItem.`PlaylistId`
    HAVING COUNT(DISTINCT queueItem.`GuildId`) > 1
) AS invalidPlaylist;

INSERT INTO `__PostUpgradeChecks`
SELECT 72, 'Playlist ownership', 'Playlist GuildId disagrees with queue items',
       0, COUNT(DISTINCT playlist.`Id`), COUNT(DISTINCT playlist.`Id`) = 0
FROM `GuildQueuePlaylists` AS playlist
INNER JOIN `GuildQueueItems` AS queueItem ON queueItem.`PlaylistId` = playlist.`Id`
WHERE queueItem.`GuildId` <> playlist.`GuildId`;

-- Results ------------------------------------------------------------------

SELECT
    `CheckGroup`,
    `CheckName`,
    `ExpectedValue`,
    `ActualValue`,
    CASE WHEN `Passed` = 1 THEN 'PASS' ELSE 'FAIL' END AS `Status`
FROM `__PostUpgradeChecks`
ORDER BY `CheckOrder`;

SELECT
    COUNT(*) AS `TotalChecks`,
    SUM(`Passed` = 1) AS `PassedChecks`,
    SUM(`Passed` = 0) AS `FailedChecks`,
    CASE WHEN SUM(`Passed` = 0) = 0 THEN 'PASS' ELSE 'FAIL' END AS `OverallStatus`
FROM `__PostUpgradeChecks`;

DROP TEMPORARY TABLE `__PostUpgradeChecks`;
