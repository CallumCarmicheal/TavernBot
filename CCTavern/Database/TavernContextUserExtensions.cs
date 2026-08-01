using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DSharpPlus.Entities;

using Microsoft.EntityFrameworkCore;

namespace CCTavern.Database;

public readonly record struct ResolvedGuildUser(TavernUser User, GuildUser GuildUser);

public static class TavernContextExtensions {
    
}
