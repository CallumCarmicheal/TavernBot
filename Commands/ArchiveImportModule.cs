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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class ArchiveImportModule : BaseCommandModule {
        private static string[] messagePrefixes = new[] {
            "<<!play", "_play", "+play", ";;play"
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

        [Command("archive:import")]
        public async Task StartImporting(CommandContext ctx, ulong firstMessageId) {
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

            //var importStatus = await ctx.RespondAsync($"Importer Status: `Warming up`.");

            var db = new TavernContext();
            //await db.Database.ExecuteSqlAsync($"TRUNCATE TABLE ArchivedTracks");

            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            // Get all the messages in the server
            int amountOfMessages = 100;

            List<DiscordMessage> discordMessages;
            DiscordMessage? first, last;
            ulong lastMessageId = firstMessageId; // ccArchive?archive:import 628641259770347541
            int count = 1;
            
            ulong processedTracks = 0;
            int   processedUniqueUsers = 0;
            //await importStatus.ModifyAsync($"Importer Status: `Processing message`.");

            Dictionary<ulong, ulong> cachedUserIds = new Dictionary<ulong, ulong>();

            for ( ; ; ) {
                var messages = (await ctx.Channel.GetMessagesAfterAsync(lastMessageId, amountOfMessages)).ToList();

                last = messages.Last();
                lastMessageId = last.Id;

                int x = 0;
                foreach (var msg in messages) {
                    var contents = msg.Content.Trim();

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
                                if (await processMessage(ctx, db, msg, dbUserId, split[1]))
                                    processedTracks++;

                                break;
                            }
                        }
                    }

                    // if (x % 10 == 0) {
                    //     logger.LogInformation($"Importer Status: `Processing` [{x,2}/{amountOfMessages}] (`{processedTracks}` processed, from `{processedUniqueUsers}` users), B:{amountOfMessages}, "
                    //          + $"id: {msg.Id}, "
                    //          + $"by `{msg.Author.Username}` "
                    //          + $"at {msg.CreationTimestamp} | {msg.Content}");
                    // }

                    x++;
                }


                Console.WriteLine($"{processedTracks,4} {processedUniqueUsers,2} | {last.Id}: {last.Author.Username}, {last.CreationTimestamp}, {last.Content}");
                //await Task.Delay(500);
                await db.SaveChangesAsync();
            }

            return; 

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
                                if (await processMessage(ctx, db, msg, dbUserId, split[1]))
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
            }
        }

        private async Task<bool> processMessage(CommandContext ctx, TavernContext db, DiscordMessage msg, ulong dbUserId, string search) {

            // Parse the track
            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any()) {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return false;
            }

            var voiceState = ctx.Member?.VoiceState;
            var channel = voiceState?.Channel;
            if (voiceState == null || channel == null || voiceState.Channel.GuildId != ctx.Guild.Id) {
                await ctx.RespondAsync("Not in voice channel of this guild.");
                return false;
            }

            if (channel.Type != ChannelType.Voice) {
                await ctx.RespondAsync("Impossible error but I dunno we got here somehow, Not a valid voice channel.");
                return false;
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
                return true;
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

            db.ArchivedTracks.Add(archive);
            return true;
        }
    }
}
