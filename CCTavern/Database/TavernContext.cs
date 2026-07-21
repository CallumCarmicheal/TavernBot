using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DSharpPlus.Entities;
using DSharpPlus.Net.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;

namespace CCTavern.Database;

public partial class TavernContext : DbContext
{
    public DbSet<Guild> Guilds { get; set; }
    public DbSet<GuildQueueItem> GuildQueueItems { get; set; }
    public DbSet<CachedUser> CachedUsers { get; set; }
    public DbSet<GuildQueuePlaylist> GuildQueuePlaylists { get; set; }
    public DbSet<TrackPlaylistPosition> TrackPlaylistPositions { get; set; }
    public DbSet<ArchivedTrack> ArchivedTracks { get; set; }
    public DbSet<ArchivedMessage> ArchivedMessages { get; set; }


    public TavernContext() { }
    public TavernContext(DbContextOptions<TavernContext> options)
        : base(options) { }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { 
        // This means we are being invoked by Migration nuget commands.
        if (Program.Settings == null) 
            Program.ReloadSettings();

        // Ensure we have a configuration
        System.Diagnostics.Debug.Assert(Program.Settings != null);

        // Choose database engine
        if (Program.Settings.DatabaseEngine == "mssql") 
            optionsBuilder.UseSqlServer(Program.Settings.DatabaseConnectionString);
        else 
            optionsBuilder.UseMySQL(Program.Settings.DatabaseConnectionString);

        // Enable logging
        if (Program.Settings.LogDatabaseQueries)
            optionsBuilder.UseLoggerFactory(Program.LoggerFactory);
    }

    #region Entity Framework: Model Configuration and Events

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

        modelBuilder.Entity<GuildQueuePlaylist>()
            .ToTable("GuildQueuePlaylists")
            .HasKey(t => t.Id);

        /* Archived Tracks */ {
            modelBuilder.Entity<ArchivedTrack>()
                .Property(t => t.Id)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<ArchivedTrack>()
                .ToTable("ArchivedTracks")
                .HasKey(t => t.Id);
        }

        /*Guild Queue Item*/ {
            modelBuilder.Entity<GuildQueueItem>()
                .Property(t => t.Id)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<GuildQueueItem>()
                .HasOne(qi => qi.RequestedBy)
                    .WithMany(usr => usr.RequestedSongs)
                .HasForeignKey(qi => qi.RequestedById)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<GuildQueueItem>()
                .HasOne(qi => qi.DeletedBy)
                    .WithMany(usr => usr.DeletedSongs)
                .HasForeignKey(qi => qi.DeletedById)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<GuildQueueItem>()
                .HasOne(qi => qi.Playlist)
                    .WithMany(pl => pl.Songs)
                .HasForeignKey(qi => qi.PlaylistId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<GuildQueueItem>()
                .HasOne(qi => qi.Guild)
                    .WithMany(guild => guild.Tracks)
                .HasForeignKey(qi => qi.GuildId)
                    .IsRequired(true)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GuildQueueItem>()
                .ToTable("GuildQueueItems")
                .HasKey(t => t.Id);
        }

        OnModelCreatingPartial(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) {
        updateChangeTrackerDates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) {
        updateChangeTrackerDates();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) {
        updateChangeTrackerDates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges() {
        updateChangeTrackerDates();
        return base.SaveChanges();
    }

    private void updateChangeTrackerDates() {
        var entries = ChangeTracker
            .Entries()
            .Where(e => (e.Entity is BaseDbEntity)
                     && (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var entityEntry in entries) {
            // Set the creation date
            if (entityEntry.State == EntityState.Added)
                ((BaseDbEntity)entityEntry.Entity).CreatedAt = DateTime.Now;
            // Update the modified date
            else if (entityEntry.State == EntityState.Modified)
                ((BaseDbEntity)entityEntry.Entity).UpdatedAt = DateTime.Now;
        }
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    #endregion

    #region Query Functions

    public async Task<CachedUser> GetOrCreateCachedUser(Guild guild, DiscordMember user) {
        CachedUser cachedUser;

        var query = CachedUsers.Where(x => x.UserId == user.Id && x.GuildId == guild.Id);

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

        if (cachedUser.DisplayName != user.DisplayName) {
            cachedUser.DisplayName = user.DisplayName;
            await SaveChangesAsync();
        }

        return cachedUser;
    }

    public async Task<Guild> GetOrCreateDiscordGuild(DiscordGuild guild) {
        Guild dbGuild;
        
        var query = Guilds
            .Where(x => x.Id == guild.Id);

        // if (includeTracks) query = query.Include(x => x.Tracks);

        if (await query.AnyAsync() == false) {
            dbGuild = new Guild() {
                Id = guild.Id,
                Name = guild.Name,
                IsPlaying = false,
                TrackCount = 0
            };

            Guilds.Add(dbGuild);
            await SaveChangesAsync();
        } else dbGuild = await query.FirstAsync();

        return dbGuild;
    }

    public async Task<Guild?> GetGuild(ulong guildId) {
        var query = Guilds
            .Where(x => x.Id == guildId);

        if (await query.AnyAsync() == false) 
            return null;

        return await query.FirstAsync();
    }


    #endregion

}
