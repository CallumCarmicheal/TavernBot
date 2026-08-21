using DSharpPlus.Entities;

using Microsoft.EntityFrameworkCore;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Database;

public readonly record struct ResolvedGuildUser(TavernUser User, GuildUser GuildUser);

public static class TavernContextExtensions {

}