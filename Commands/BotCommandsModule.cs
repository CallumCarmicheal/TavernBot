﻿using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class BotCommandsModule : BaseCommandModule {

        [Command("version")]
        public async Task PrintStatus(CommandContext ctx) {
            await ctx.RespondAsync("Version: " + Program.VERSION_Full + "\nGit Hash: " + Program.VERSION_Git_WithBuild);
        }
    }
}
