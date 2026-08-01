# CCTavern Discord Music Bot
A discord music bot making use of DSharp+ and Lavalink.
This bot is written with the intention of logging / storing every song ever played in it, for sentimental reason and also just so we can randomize the bot and curate a playlist.

***This bot is not on the docker hub, compile it locally***

# Why C# & DSharp+? 
Because I love C# and tried writing a music bot in Javascript several times but just did not like how unpredictable my bad code can be with not statically checked.
I like the speed and type-safety offered by C# with my bad code.

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

## Running with Docker

The image must currently be built locally use the provided build-docker.sh script or run the commands below:

```shell
git clone https://github.com/CallumCarmicheal/TavernBot.git
cd TavernBot
docker build -t cctavern-image -f Dockerfile .
```

Copy `docker-compose.example` to `docker-compose.yml` and place your
`Configuration.json` beside it. The Compose file mounts the configuration at
`/app/Configuration.json`, configures ASP.NET Core to listen on port `10001`,
and publishes that port on the host.

```shell
cp docker-compose.example docker-compose.yml
docker compose up
```

After verifying that the bot starts successfully, press Ctrl+Z to detach or press Ctrl+C and start it in
the background:

```shell
docker compose up -d
```

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
