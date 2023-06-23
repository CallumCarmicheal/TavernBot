# CCTavern Discord Music Bot
A discord music bot making use of DSharp+ and Lavalink.
This bot is written with the intention of logging / storing every song ever played in it, for sentimental reason and also just so we can randomize the bot and curate a playlist.

***This bot is not on the docker hub... yet (if it ever does)***

# Why C# & DSharp+? 
Because I love C# and tried writing a music bot in Javascript several times but just did not like how unpredictable my bad code can be with not statically checked.
I like the speed and type-safety offered by C#.

# How to run this bot?
First the bot uses MySQL as its database, this is hard coded. You can change this by going to `Database/TavernContext.cs` and modifying the `OnConfiguring` method.
```cs
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { 
    optionsBuilder.UseMySQL(Program.Settings.MySQLConnectionString);
//  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
    optionsBuilder.UseLoggerFactory(Program.LoggerFactory);
}
```

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
  "mysqlConnectionString": "Server=192.168.0.2; Port=3306; Database=discord__musicbot_tavern; Uid=discord__musicbot_tavern; Pwd=DATABASEPASSWORD;",
  "lavalink": {
    "hostname": "192.168.0.2",
    "port": 8200,
    "password": "lavalinkpassword"
  }
}
```