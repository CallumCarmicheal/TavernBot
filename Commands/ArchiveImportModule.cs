using CCTavern.Database;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Lavalink;

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


        [Command("archive:import")]
        public async Task StartImporting(CommandContext ctx) {
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

            // btnYes was selected, start the import
            await buttonMessage.DeleteAsync();
            await ctx.RespondAsync("IMPORTER GOES BRRRRRRRRR...");

            var importStatus = await ctx.RespondAsync($"Importer Status: `Warming up`.");

            // Get all the messages in the server
            int amountOfMessages = 100;

            IReadOnlyList<DiscordMessage>? discordMessages;
            DiscordMessage? last, first;
            ulong currentMessageId = 628641259770347541;
            int count = 0;

            ulong processedTracks = 0;
            await importStatus.ModifyAsync($"Importer Status: `Processing message`.");

            var db = new TavernContext();
            await db.Database.ExecuteSqlAsync($"TRUNCATE TABLE ArchivedTracks");

            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            Dictionary<ulong, ulong> cachedUserIds = new Dictionary<ulong, ulong>();

        loadMessages:
            discordMessages = await ctx.Channel.GetMessagesAfterAsync(currentMessageId, amountOfMessages);
            count = discordMessages.Count();
            first = discordMessages.FirstOrDefault();
            last  = discordMessages.LastOrDefault();

            if (count == 0 || first == null) {
                await importStatus.ModifyAsync($"Importer Status: `Finished`, no more messages after https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{currentMessageId}.");
                return;
            }

            await importStatus.ModifyAsync($"Importer Status: `Processing` [1/{amountOfMessages}] (`{processedTracks}` processed), Current batch of {amountOfMessages}, Date = `{first.CreationTimestamp.ToString()}` " 
                + $"by `{first.Author.Username}`\n"
                + $"https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{first.Id}");

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

                // Check if we are the last message
                if (discordMessages.Equals(last)) 
                    // Set the next iteration
                    currentMessageId = msg.Id;

                if (x % 25 == 0) {
                    await importStatus.ModifyAsync($"Importer Status: `Processing` [{x+1}/{amountOfMessages}] (`{processedTracks}` processed), Current batch of {amountOfMessages}, Date = `{first.CreationTimestamp.ToString()}` "
                       + $"by `{first.Author.Username}`\n"
                       + $"https://discordapp.com/channels/{ctx.Guild.Id}/{ctx.Channel.Id}/{first.Id}");
                }
            }

            await db.SaveChangesAsync();
            goto loadMessages;
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
                    || loadResult.LoadResultType == LavalinkLoadResultType.NoMatches) {
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
            };

            db.ArchivedTracks.Add(archive);
            return true;
        }
    }
}
