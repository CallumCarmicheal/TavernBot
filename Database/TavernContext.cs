using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

using DSharpPlus.Entities;
using DSharpPlus.Net.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCTavern.Database;

public partial class TavernContext : DbContext
{
    public DbSet<Guild> Guilds { get; set; }
    public DbSet<GuildQueueItem> GuildQueueItems { get; set; }
    public DbSet<CachedUser> CachedUsers { get; set; }


    public TavernContext() { }
    public TavernContext(DbContextOptions<TavernContext> options)
        : base(options) { }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { 
        optionsBuilder.UseMySQL(Program.Settings.MySQLConnectionString);
        optionsBuilder.UseLoggerFactory(Program.LoggerFactory);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        /* Cached User */ {
            modelBuilder.Entity<CachedUser>()
                .Property(t => t.Id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<CachedUser>()
                .ToTable("CachedUsers")
                .HasKey(t => t.Id);
        }

        modelBuilder.Entity<Guild>()
           .ToTable("Guilds")
           .HasKey(t => t.Id);

        /*Guild Queue Item*/ {
            modelBuilder.Entity<GuildQueueItem>()
                .Property(t => t.Id)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<GuildQueueItem>()
                .HasOne(qi => qi.RequestedBy)
                .WithMany(usr => usr.RequestedSongs)
                .HasForeignKey(p => p.RequestedById)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<GuildQueueItem>()
               .ToTable("GuildQueueItems")
               .HasKey(t => t.Id);
            
        }

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);


    public async Task<CachedUser> GetOrCreateCachedUser(Guild guild, DiscordMember user) {
        CachedUser cachedUser;

        var query = CachedUsers.Where(x => x.Id == user.Id && x.GuildId == guild.Id);

        if (await query.AnyAsync() == false) {
            cachedUser = new CachedUser() {
                UserId = user.Id,
                DisplayName = user.DisplayName,
                Username = user.Username,
                GuildId = guild.Id
            };

            CachedUsers.Add(cachedUser);
            await SaveChangesAsync();
        } else cachedUser = await query.FirstAsync();

        return cachedUser;
    }

    public async Task<Guild> GetOrCreateDiscordGuild(DiscordGuild guild) {
        Guild dbGuild;
        
        var query = Guilds.Where(x => x.Id == guild.Id);
        if (await query.AnyAsync() == false) {
            dbGuild = new Guild() {
                Id = guild.Id,
                Name = guild.Name,
            };

            Guilds.Add(dbGuild);
            await SaveChangesAsync();
        } else dbGuild = await query.FirstAsync();

        return dbGuild;
    }
}
