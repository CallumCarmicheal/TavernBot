# CCTavern Discord Music Bot
A discord music bot making use of DSharp+ and Lavalink.
This bot is written with the intention of logging / storing every song ever played in it, for sentimental reason and also just so we can randomize the bot and curate a playlist.

***This bot is not on the docker hub... yet (if it ever does)***

# Why C# & DSharp+? 
Because I love C# and tried writing a music bot in Javascript several times but just did not like how unpredictable my bad code can be with not statically checked.
I like the speed and type-safety offered by C#.

# How to run this bot?
CCTavern uses MariaDB/MySQL by default and applies Entity Framework migrations automatically during startup.

## Database migrations

Always back up an existing database before deploying a version containing new migrations.

For a new database with no tables, startup applies the complete migration chain automatically. This includes migrations which remove transitional schema because a fresh database has no existing data to protect.

For an existing database, destructive migrations require explicit approval. The `Users`/`GuildUsers` upgrade uses this process:

1. Start the updated bot with `applyDestructiveDatabaseMigrations` set to `false`. Startup applies `IntroduceUsersAndGuildUsers`, migrates the data, and leaves `CachedUsers` and the legacy actor columns available for validation.
2. Run [`PostUpgradeUsersAndGuildUsers.sql`](scripts/PostUpgradeUsersAndGuildUsers.sql) against the database. Continue only when it reports `FailedChecks = 0` and `OverallStatus = PASS`.
3. Back up the validated database, set `applyDestructiveDatabaseMigrations` to `true`, and restart the bot. Startup then applies `RemoveCachedUsers`.
4. After the migration succeeds, return `applyDestructiveDatabaseMigrations` to `false`.

The post-upgrade script is read-only apart from a session-scoped temporary table. It is only applicable between the expand and contract migrations, while `CachedUsers` still exists.

When running as a docker container (you need to build this yourself)
```
git clone https://github.com/CallumCarmicheal/TavernBot.git
cd TavernBot
docker build -t cctavern-image -f Dockerfile .

# Goto or make a folder to store the docker file
# <Create the docker-compose.yml file>
docker-compose up
#Verify it works then hit ctrl c
docker-compose up -d
```

Example docker-compose.yml in the repository.

Example Configuration.json:
```json
{
  "discordToken": "DiscordToken",
  "dbEngine": "mysql",
  "dbConnectionString": "Server=192.168.0.2; Port=3306; Database=discord__musicbot_tavern; Uid=discord__musicbot_tavern; Pwd=DATABASEPASSWORD;",
  "applyDestructiveDatabaseMigrations": false,
  "lavalink": {
    "hostname": "192.168.0.2",
    "port": 8200,
    "password": "lavalinkpassword"
  }
}
```
