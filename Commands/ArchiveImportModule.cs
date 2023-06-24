using CCTavern.Database;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class ArchiveImportModule : BaseCommandModule {

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
            } else if (result.Result.Id == btnNo.CustomId) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("Alright, fine keep your secrets.");
            } else if (result.Result.Id != btnYes.CustomId) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("Sadly at this time I am unable to verify what decision you made.");
            }

            // btnYes was selected, start the import
            await buttonMessage.DeleteAsync();
            await ctx.RespondAsync("IMPORTER GOES BRRRRRRRRR...");

            var importStatus = ctx.RespondAsync($"Importer Status: `Warming up`.");
        }
    }
}
