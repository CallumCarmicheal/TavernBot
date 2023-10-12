using CCTavern.Database;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Lavalink;

using K4os.Hash.xxHash;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class ArchiveImportModule : BaseCommandModule {
        private static string[] messagePrefixes = new[] {
            "<<!play", "_play", "+play", ";;play", "!play"
        };

        private ILogger _logger;
        private ILogger logger {
            get {
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<MusicCommandModule>();
                return _logger;
            }
        }

        [Command("archive:d")]
        public async Task DeleteMessage(CommandContext ctx, DiscordMessage message) {
            if (message != null) 
                await message.DeleteAsync("Clearing bot commands");
        }

        [Command("archive:processMusic")]
        public async Task StartImportingAndProcess(CommandContext ctx, ulong? firstMessageId = null) {
            // Are you sure? 
            long ticks = DateTime.Now.Ticks;
            byte[] bytes = BitConverter.GetBytes(ticks);
            string interactionId = Convert.ToBase64String(bytes).Replace('+', '_').Replace('/', '-').TrimEnd('=');

            var btnYes = new DiscordButtonComponent(ButtonStyle.Success, $"importAll{interactionId}", "COWABUNGA IT IS");
            var btnNo = new DiscordButtonComponent(ButtonStyle.Danger, $"cancel{interactionId}", "On second thought...");

            var builder = new DiscordMessageBuilder()
                .WithContent("Are you sure you want to import all songs from the dawn of time. It will be awhile...")
                .AddComponents(btnYes, btnNo);

            var buttonMessage = await ctx.RespondAsync(builder);

            var interactivity = ctx.Client.GetInteractivity();
            var result = await interactivity.WaitForButtonAsync(buttonMessage, TimeSpan.FromSeconds(30));

            if (result.TimedOut) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("Interaction timed out, I ain't waiting around yer indecisive ass.");
                return;
            } else if (result.Result.Id == btnNo.CustomId) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("https://tenor.com/view/lord-of-the-rings-frodo-alright-then-keep-your-secrets-smiling-gif-26462584");
                //await ctx.RespondAsync("Alright, fine keep your secrets.");
                return;
            } else if (result.Result.Id != btnYes.CustomId) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("Sadly at this time I am unable to verify what decision you made.");
                return;
            }

            var dtStart = DateTime.Now;

            // btnYes was selected, start the import
            await buttonMessage.DeleteAsync();
            // await ctx.RespondAsync("IMPORTER GOES BRRRRRRRRR...");

            var importStatus = await ctx.RespondAsync($"Importer Status: `Warming up`.");

            // Get all the messages in the server
            int amountOfMessages = 100;

            // List<DiscordMessage> discordMessages;
            DiscordMessage? last = null;

            ulong lastMessageId;

            // Use supplied message to start off
            if (firstMessageId != null) {
                lastMessageId = firstMessageId.Value;
            }
            // Get the last added message id.
            else {
                var db = new TavernContext();
                lastMessageId = db.ArchivedTracks.OrderByDescending(x => x.MessageId).First().Id;
            }

            //ulong lastMessageId = firstMessageId; // ccArchive?archive:import 628641259770347541

            ulong processedTracks = 0;
            int processedUniqueUsers = 0;
            await importStatus.ModifyAsync($"Importer Status: `Processing message`.");

            Stopwatch swMessages = new Stopwatch();
            swMessages.Start();

            Dictionary<ulong, ulong?> cachedUserIds = new Dictionary<ulong, ulong?>();
            Dictionary<ulong, DiscordMember?> cachedDiscordMember = new Dictionary<ulong, DiscordMember?>();

            for (; ; ) {
                var db = new TavernContext();
                var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

                var startId = lastMessageId;

                // I seem to have a problem of getting duplicates, I believe this was resolved by doing `messages.OrderByDescending(x => x.Id).First();`.
                // I assume DiscordSharp was not returning an array of messaged sorted by their creation date so it was looping upon to itself.
                // this seems to have resolved the issue as the lastId of the list was not the latest message created.
                List<DiscordMessage> messages =
                    (await ctx.Channel.GetMessagesAfterAsync(lastMessageId, amountOfMessages))
                    .GroupBy(x => x.Id)
                    .Select(g => g.First())
                    .OrderBy(x => x.Id)
                    .ToList();


                // Completed importing all songs
                if (messages.Count == 0) {
                    break;
                }


                last = messages.OrderByDescending(x => x.Id).First();
                lastMessageId = last.Id;

                int x = 0;

                List<ArchivedTrack> archivedTracksToAdd = new List<ArchivedTrack>();
                List<ArchivedMessage> archivedMessagesToAdd = new List<ArchivedMessage>();

                foreach (var msg in messages) {
                    var contents = msg.Content.Trim();

                    DiscordMember? member = null;

                    if (cachedDiscordMember.ContainsKey(msg.Author.Id)) {
                        member = cachedDiscordMember[msg.Author.Id];
                    } else {
                        try {
                            member = await ctx.Guild.GetMemberAsync(msg.Author.Id);
                            cachedDiscordMember.Add(msg.Author.Id, member);
                        } catch {
                            cachedDiscordMember.Add(msg.Author.Id, null);
                        }
                    }

                    CachedUser? dbUser = null;

                    ulong? dbUserId = null;
                    if (cachedUserIds.ContainsKey(msg.Author.Id) == false) {
                        if (member == null) {
                            dbUserId = null;
                        }
                        else {
                            dbUser = await db.GetOrCreateCachedUser(guild, member);

                            cachedUserIds.Add(msg.Author.Id, dbUser.Id);
                            dbUserId = dbUser.Id;
                            processedUniqueUsers = cachedUserIds.Count;
                        }
                    } else {
                        dbUserId = cachedUserIds[msg.Author.Id];
                    }

                    var archivedMessage = new ArchivedMessage();
                    archivedMessage.AuthorId = dbUserId;
                    archivedMessage.MessageId = msg.Id;
                    archivedMessage.GuildId = guild.Id;
                    archivedMessage.MessageContents = msg.Content;
                    archivedMessage.DateMessageCreated = msg.CreationTimestamp.DateTime;

                    foreach (var pfx in messagePrefixes) {
                        bool startsWith = contents.StartsWith(pfx);
                        if (startsWith) {
                            var split = contents.Split(pfx);

                            if (split.Length > 1) {
                                archivedMessage.ContainsPrefix = true;

                                // If we have processed the track
                                (var processed, var archiveTrack) = await processMessage(ctx, db, msg, dbUserId, split[1]);
                                if (processed && archiveTrack != null) {
                                    processedTracks++;
                                    archivedTracksToAdd.Add(archiveTrack);
                                }

                                break;
                            }
                        }
                    }

                    archivedMessagesToAdd.Add(archivedMessage);

                    if (swMessages.ElapsedMilliseconds >= (8 * 1000)) {
                        await importStatus.ModifyAsync($"Importer Status: `Processing` [{x + 1}/{amountOfMessages}] (`{processedTracks}` processed), Current batch of {amountOfMessages}, "
                            + $"by `{msg.Author.Username}` "
                            + $"at {Formatter.Timestamp(msg.CreationTimestamp)} @ {Formatter.Timestamp(msg.CreationTimestamp, TimestampFormat.ShortDateTime)}\n"
                            + $"https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{msg.Id}");

                        if (swMessages.IsRunning)
                            swMessages.Reset();
                        swMessages.Start();
                    }

                    x++;
                }


                Console.WriteLine($"{processedTracks,4} {processedUniqueUsers,2} | {last.Id}: {last.Author.Username}, {last.CreationTimestamp}, {last.Content}");
                await db.ArchivedTracks.AddRangeAsync(archivedTracksToAdd);
                await db.ArchivedMessages.AddRangeAsync(archivedMessagesToAdd);
                await db.SaveChangesAsync();

                archivedTracksToAdd.Clear();
                archivedMessagesToAdd.Clear();
            }

            string lastMessageText = "";

            if (last != null) {
                lastMessageText =
                    $"by `{last.Author.Username}` "
                    + $"at {Formatter.Timestamp(last.CreationTimestamp)} @ {Formatter.Timestamp(last.CreationTimestamp, TimestampFormat.ShortDateTime)}\n"
                    + $"https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{last.Id}";
            }

            await importStatus.ModifyAsync($"Importer Status: `Finished`, Processed `{processedTracks}` tracks, from `{processedUniqueUsers}` users, Last procssed message: "
                + lastMessageText);
        }

        [Command("archive:import")]
        public async Task StartImporting(CommandContext ctx, ulong? firstMessageId = null) {
            // Are you sure? 
            long ticks = DateTime.Now.Ticks;
            byte[] bytes = BitConverter.GetBytes(ticks);
            string interactionId = Convert.ToBase64String(bytes).Replace('+', '_').Replace('/', '-').TrimEnd('=');

            var btnYes = new DiscordButtonComponent(ButtonStyle.Success, $"importAll{interactionId}", "COWABUNGA IT IS");
            var btnNo  = new DiscordButtonComponent(ButtonStyle.Danger, $"cancel{interactionId}", "On second thought...");

            var builder = new DiscordMessageBuilder()
                .WithContent("Are you sure you want to import all songs from the dawn of time. It will be awhile...")
                .AddComponents(btnYes, btnNo);

            var buttonMessage = await ctx.RespondAsync(builder);

            var interactivity = ctx.Client.GetInteractivity();
            var result = await interactivity.WaitForButtonAsync(buttonMessage, TimeSpan.FromSeconds(30));

            if (result.TimedOut) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("Interaction timed out, I ain't waiting around yer indecisive ass.");
                return;
            } else if (result.Result.Id == btnNo.CustomId) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("https://tenor.com/view/lord-of-the-rings-frodo-alright-then-keep-your-secrets-smiling-gif-26462584");
                //await ctx.RespondAsync("Alright, fine keep your secrets.");
                return;
            } else if (result.Result.Id != btnYes.CustomId) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("Sadly at this time I am unable to verify what decision you made.");
                return;
            }

            var dtStart = DateTime.Now;

            // btnYes was selected, start the import
            await buttonMessage.DeleteAsync();
            // await ctx.RespondAsync("IMPORTER GOES BRRRRRRRRR...");

            var importStatus = await ctx.RespondAsync($"Importer Status: `Warming up`.");

            //var db = new TavernContext();
            //    //await db.Database.ExecuteSqlAsync($"TRUNCATE TABLE ArchivedTracks");
            //var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            // Get all the messages in the server
            int amountOfMessages = 100;

            // List<DiscordMessage> discordMessages;
            // DiscordMessage? first = null;
            DiscordMessage? last = null;

            ulong lastMessageId;

            // Use supplied message to start off
            if (firstMessageId != null) {
                lastMessageId = firstMessageId.Value;
            }
            // Get the last added message id.
            else {
                var db = new TavernContext();
                lastMessageId = db.ArchivedTracks.OrderByDescending(x => x.MessageId).First().Id;
            }

            //ulong lastMessageId = firstMessageId; // ccArchive?archive:import 628641259770347541
            
            ulong processedTracks = 0;
            int   processedUniqueUsers = 0;
            await importStatus.ModifyAsync($"Importer Status: `Processing message`.");

            Stopwatch swMessages = new Stopwatch();
            swMessages.Start();

            Dictionary<ulong, ulong> cachedUserIds = new Dictionary<ulong, ulong>();

            for ( ; ; ) {
                var db = new TavernContext();
                var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

                var startId = lastMessageId;

                

                // I seem to have a problem of getting duplicates, I believe this was resolved by doing `messages.OrderByDescending(x => x.Id).First();`.
                // I assume DiscordSharp was not returning an array of messaged sorted by their creation date so it was looping upon to itself.
                // this seems to have resolved the issue as the lastId of the list was not the latest message created.
                List<DiscordMessage> messages = 
                    (await ctx.Channel.GetMessagesAfterAsync(lastMessageId, amountOfMessages))
                    .GroupBy(x => x.Id)
                    .Select(g => g.First())
                    .OrderBy(x => x.Id)
                    .ToList();


                // Completed importing all songs
                if (messages.Count == 0) {
                    break;
                }


                last = messages.OrderByDescending(x => x.Id).First();
                lastMessageId = last.Id;

                int x = 0;

                List<ArchivedTrack> archivedTracksToAdd = new List<ArchivedTrack>();

                foreach (var msg in messages) {
                    var contents = msg.Content.Trim();

                    foreach (var pfx in messagePrefixes) {
                        bool startsWith = contents.StartsWith(pfx);
                        if (startsWith) {
                            var split = contents.Split(pfx);

                            if (split.Length > 1) {
                                ulong dbUserId = 0;
                                if (cachedUserIds.ContainsKey(msg.Author.Id) == false) {
                                    var member = await ctx.Guild.GetMemberAsync(msg.Author.Id);
                                    var dbUser = await db.GetOrCreateCachedUser(guild, member);
                                    cachedUserIds.Add(msg.Author.Id, dbUser.Id);
                                    dbUserId = dbUser.Id;
                                    processedUniqueUsers = cachedUserIds.Count;
                                } else {
                                    dbUserId = cachedUserIds[msg.Author.Id];
                                }

                                // If we have processed the track
                                (var processed, var archiveTrack) = await processMessage(ctx, db, msg, dbUserId, split[1]);
                                if (processed && archiveTrack != null) {
                                    processedTracks++;
                                    archivedTracksToAdd.Add(archiveTrack);
                                }

                                break;
                            }
                        }
                    }

                    //if (x % 10 == 0) {
                    //    logger.LogInformation($"Importer Status: `Processing` [{x,2}/{amountOfMessages}] (`{processedTracks}` processed, from `{processedUniqueUsers}` users), B:{amountOfMessages}, "
                    //         + $"id: {msg.Id}, "
                    //         + $"by `{msg.Author.Username}` "
                    //         + $"at {msg.CreationTimestamp} | {msg.Content}");
                    //}

                    if (swMessages.ElapsedMilliseconds >= (8 * 1000)) {
                        await importStatus.ModifyAsync($"Importer Status: `Processing` [{x+1}/{amountOfMessages}] (`{processedTracks}` processed, from `{processedUniqueUsers}` users), Current batch of {amountOfMessages}, " 
                            + $"by `{msg.Author.Username}` "
                            + $"at {Formatter.Timestamp(msg.CreationTimestamp)} @ {Formatter.Timestamp(msg.CreationTimestamp, TimestampFormat.ShortDateTime)}\n"
                            + $"https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{msg.Id}");

                        if (swMessages.IsRunning)
                            swMessages.Reset();
                        swMessages.Start();
                    }

                    x++;
                }


                Console.WriteLine($"{processedTracks,4} {processedUniqueUsers,2} | {last.Id}: {last.Author.Username}, {last.CreationTimestamp}, {last.Content}");
                await db.ArchivedTracks.AddRangeAsync(archivedTracksToAdd);
                await db.SaveChangesAsync();

                archivedTracksToAdd.Clear();
            }

            string lastArchiveTrackMessage = "";
            if (last != null) {
                lastArchiveTrackMessage =
                    $"Last procssed message by `{last.Author.Username}` "
                    + $"at {Formatter.Timestamp(last.CreationTimestamp)} @ {Formatter.Timestamp(last.CreationTimestamp, TimestampFormat.ShortDateTime)}\n"
                    + $"https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{last.Id}";
            }

            await importStatus.ModifyAsync($"Importer Status: `Finished`, Processed `{processedTracks}` tracks, "
                + $"from `{processedUniqueUsers}` users, {lastArchiveTrackMessage}");

            /*
            while (count != 0) {
                discordMessages = (await ctx.Channel.GetMessagesAfterAsync(lastMessageId, amountOfMessages)).ToList();
                count = discordMessages.Count();

                if (count == 0) {
                    var dtEnd = DateTime.Now;
                    var diff = dtStart - dtEnd;

                    //await importStatus.ModifyAsync($"Importer Status: `Finished`, no more messages after https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{currentMessageId}.  Time taken: " + diff.ToString());
                    return;
                }

                first = discordMessages.First();
                last = discordMessages.Last();

                if (first.Id != last?.Id) 
                    lastMessageId = last.Id;

                //await importStatus.ModifyAsync($"Importer Status: `Processing.f` [1/{amountOfMessages}] (`{processedTracks}` processed, from `{processedUniqueUsers}` users), Current batch of {amountOfMessages}, " 
                //    + $"by `{first.Author.Username}` "
                //    + $"at {Formatter.Timestamp(first.CreationTimestamp)} @ {Formatter.Timestamp(first.CreationTimestamp, TimestampFormat.ShortDateTime)}\n"
                //    + $"https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{first.Id}");

                for (int x = 0; x < count; x++) {
                    var msg = discordMessages[x];
                    var contents = msg.Content.Trim();

                    if (string.IsNullOrWhiteSpace(contents))
                        continue;

                    foreach (var pfx in messagePrefixes) {
                        if (contents.StartsWith(pfx)) {
                            var split = contents.Split(pfx);

                            if (split.Length > 1) {
                                ulong dbUserId = 0;
                                if (cachedUserIds.ContainsKey(msg.Author.Id) == false) {
                                    var member = await ctx.Guild.GetMemberAsync(msg.Author.Id);
                                    var dbUser = await db.GetOrCreateCachedUser(guild, member);
                                    cachedUserIds.Add(msg.Author.Id, dbUser.Id);
                                    dbUserId = dbUser.Id;
                                    processedUniqueUsers = cachedUserIds.Count;
                                } else {
                                    dbUserId = cachedUserIds[msg.Author.Id];
                                }

                                // If we have processed the track
                                (var processed, var archiveMessage) = (await processMessage(ctx, db, msg, dbUserId, split[1]));
                                if (processed)
                                    processedTracks++;

                                break;
                            }
                        }
                    }

                    if (x % 10 == 0) {
                        logger.LogInformation($"Importer Status: `Processing` [{x,2}/{amountOfMessages}] (`{processedTracks}` processed, from `{processedUniqueUsers}` users), B:{amountOfMessages}, "
                             + $"id: {msg.Id}, "
                             + $"by `{msg.Author.Username}` "
                             + $"at {msg.CreationTimestamp} | {msg.Content}");
                    }

                    // if (x == 99) {
                    //     await importStatus.ModifyAsync($"Importer Status: `Processing` [{x+1}/{amountOfMessages}] (`{processedTracks}` processed, from `{processedUniqueUsers}` users), Current batch of {amountOfMessages}, " 
                    //         + $"by `{msg.Author.Username}` "
                    //         + $"at {Formatter.Timestamp(msg.CreationTimestamp)} @ {Formatter.Timestamp(msg.CreationTimestamp, TimestampFormat.ShortDateTime)}\n"
                    //         + $"https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{msg.Id}");
                    // }

                    //// Check if we are the last message
                    //if (x >= (count - 1))
                    //    // Set the next iteration
                    //    lastMessageId = msg.Id;
                }

                await db.SaveChangesAsync();
            } //*/
        }

        private async Task<(bool, ArchivedTrack?)> processMessage(CommandContext ctx, TavernContext db, DiscordMessage msg, ulong? dbUserId, string search) {

            // Parse the track
            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any()) {
                Console.WriteLine("The Lavalink connection is not established");
                return (false, null);
            }

            var voiceState = ctx.Member?.VoiceState;
            var channel = voiceState?.Channel;
            if (voiceState == null || channel == null || voiceState.Channel.GuildId != ctx.Guild.Id) {
                Console.WriteLine("Not in voice channel of this guild.");
                return (false, null);
            }

            if (channel.Type != ChannelType.Voice) {
                Console.WriteLine("Impossible error but I dunno we got here somehow, Not a valid voice channel.");
                return (false, null);
            }

            // Check if the bot is connected
            var conn = lava.GetGuildConnection(ctx.Member?.VoiceState.Guild);
            var node = lava.ConnectedNodes.Values.First();

            if (conn == null) {
                // Connect the bot
                conn = await node.ConnectAsync(channel);
            }

            LavalinkLoadResult loadResult;
            loadResult = Uri.TryCreate(search, UriKind.Absolute, out Uri? uri)
                ? await node.Rest.GetTracksAsync(uri)
                : await node.Rest.GetTracksAsync(search);

            // If something went wrong on Lavalink's end                          
            if (loadResult.LoadResultType == LavalinkLoadResultType.LoadFailed
                    //or it just couldn't find anything.
                    || loadResult.LoadResultType == LavalinkLoadResultType.NoMatches
                    || loadResult.PlaylistInfo.SelectedTrack == -1) {
                //await ctx.RespondAsync($"Track search failed for {search}.");
                return (false, null);
            }

            LavalinkTrack track;
            if (loadResult.LoadResultType == LavalinkLoadResultType.PlaylistLoaded) {
                track = loadResult.Tracks.ElementAt(loadResult.PlaylistInfo.SelectedTrack);
            } else {
                track = loadResult.Tracks.First();
            }

            var archive = new ArchivedTrack() {
                DateMessageCreated = msg.CreationTimestamp.DateTime,

                GuildId = ctx.Guild.Id,
                Length = track.Length,
                Position = 0,

                RequestedById = dbUserId,
                Title = track.Title,
                TrackString = track.TrackString,

                MessageId = msg.Id
            };

            System.Diagnostics.Debug.WriteLine($"Song Added - {msg.Id}: {msg.Author.Username}, {msg.CreationTimestamp}, ({archive.Title}) {msg.Content}", "Archiving");

            return (true, archive);
        }
    }
}
