using CCTavern.Player;

using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus;

using Lavalink4NET;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {

    internal class RadioStreamCommandModule : BaseAudioCommandModule {
        private readonly ILogger<RadioStreamCommandModule> logger;

        public RadioStreamCommandModule(MusicBotHelper mbHelper, ILogger<RadioStreamCommandModule> logger, IAudioService audioService)
                : base(audioService, mbHelper) {
            this.logger = logger;
        }

        //[Command("sverige"), Aliases("svenska", "swed", "ikea")]
        //[Description("Play music using a search")]
        //[RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        //public async Task PlaySwedishRock(CommandContext ctx, [RemainingText] string search) {
            //
        //}
    }
}
