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
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace CCTavern.Database;

public partial class TavernContext : DbContext
{
    public DbSet<Guild> Guilds { get; set; }
    public DbSet<GuildQueueItem> GuildQueueItems { get; set; }
    public DbSet<TavernUser> Users { get; set; }
    public DbSet<GuildUser> GuildUsers { get; set; }
    public DbSet<GuildQueuePlaylist> GuildQueuePlaylists { get; set; }
    public DbSet<TrackPlaylistPosition> TrackPlaylistPositions { get; set; }
    public DbSet<ArchivedTrack> ArchivedTracks { get; set; }
    public DbSet<ArchivedMessage> ArchivedMessages { get; set; }
    public DbSet<GuildEventRecord> GuildEvents { get; set; }


    public TavernContext() { }
    public TavernContext(DbContextOptions<TavernContext> options)
        : base(options) { }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
        if (optionsBuilder.IsConfigured)
            return;

        // Keep parameterless construction available for Entity Framework tooling.
        if (Program.Settings == null)
            Program.ReloadSettings();

        ConfigureOptions(optionsBuilder, Program.Settings!, Program.LoggerFactory);
    }

    internal static void ConfigureOptions(
        DbContextOptionsBuilder optionsBuilder,
        ITavernSettings settings,
        ILoggerFactory loggerFactory
    ) {
        if (settings.DatabaseEngine == "mssql")
            optionsBuilder.UseSqlServer(settings.DatabaseConnectionString);
        else {
            optionsBuilder
                .UseMySQL(settings.DatabaseConnectionString)
                .ReplaceService<IHistoryRepository, MariaDbHistoryRepository>();
        }

        if (settings.LogDatabaseQueries)
            optionsBuilder.UseLoggerFactory(loggerFactory);
    }

    #region Entity Framework: Model Configuration and Events

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        /* Global Discord users and guild-specific profiles */ {
            var user = modelBuilder.Entity<TavernUser>();
            user.ToTable("Users");
            user.HasKey(x => x.Id);
            user.Property(x => x.Id).ValueGeneratedNever();
            user.Property(x => x.Username).HasMaxLength(64).IsRequired();

            var guildUser = modelBuilder.Entity<GuildUser>();
            guildUser.ToTable("GuildUsers");
            guildUser.HasKey(x => new { x.GuildId, x.UserId });
            guildUser.Property(x => x.DisplayName).HasMaxLength(255).IsRequired();
            guildUser.HasIndex(x => x.UserId);

            guildUser.HasOne(x => x.Guild)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.GuildId)
                .OnDelete(DeleteBehavior.Cascade);

            guildUser.HasOne(x => x.User)
                .WithMany(x => x.GuildUsers)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        modelBuilder.Entity<Guild>()
           .ToTable("Guilds")
           .HasKey(t => t.Id);

        /* Append-only guild activity and audit events */ {
            var guildEvent = modelBuilder.Entity<GuildEventRecord>();
            guildEvent.ToTable("GuildEvents", table => {
                table.HasCheckConstraint(
                    "CK_GuildEvents_PayloadJson",
                    Database.IsSqlServer()
                        ? "ISJSON([PayloadJson]) = 1"
                        : "JSON_VALID(`PayloadJson`)");
            });

            guildEvent.HasKey(x => x.Id);
            guildEvent.Property(x => x.Id).ValueGeneratedOnAdd();
            guildEvent.Property(x => x.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
            guildEvent.Property(x => x.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
            guildEvent.Property(x => x.ActorUsername).HasMaxLength(64);
            guildEvent.Property(x => x.ActorDisplayName).HasMaxLength(255);
            guildEvent.Property(x => x.OccurredAtUtc)
                .HasColumnType(Database.IsSqlServer() ? "datetime2(6)" : "datetime(6)")
                .IsRequired();
            guildEvent.Property(x => x.PayloadVersion).HasDefaultValue((ushort)1).IsRequired();
            guildEvent.Property(x => x.PayloadJson)
                .HasColumnType(Database.IsSqlServer() ? "nvarchar(max)" : "json")
                .HasDefaultValue("{}")
                .IsRequired();

            guildEvent.HasIndex(x => new { x.GuildId, x.OccurredAtUtc, x.Id });
            guildEvent.HasIndex(x => new { x.GuildId, x.Kind, x.OccurredAtUtc, x.Id });
            guildEvent.HasIndex(x => new { x.ActorUserId, x.OccurredAtUtc, x.Id });
            guildEvent.HasIndex(x => x.CorrelationId);
            guildEvent.HasIndex(x => x.MessageId);
            guildEvent.HasIndex(x => x.QueueItemId);

            guildEvent.HasOne(x => x.Guild)
                .WithMany()
                .HasForeignKey(x => x.GuildId)
                .OnDelete(DeleteBehavior.Restrict);

            guildEvent.HasOne(x => x.ActorUser)
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            guildEvent.HasOne(x => x.QueueItem)
                .WithMany()
                .HasForeignKey(x => x.QueueItemId)
                .OnDelete(DeleteBehavior.Restrict);
        }

        {
            var playlist = modelBuilder.Entity<GuildQueuePlaylist>();
            playlist.ToTable("GuildQueuePlaylists");
            playlist.HasKey(t => t.Id);

            playlist.HasOne(x => x.Guild)
                .WithMany(x => x.Playlists)
                .HasForeignKey(x => x.GuildId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            playlist.HasOne(x => x.CreatedByUser)
                .WithMany(x => x.CreatedPlaylists)
                .HasForeignKey(x => x.CreatedByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        }

        /* Archived Tracks */ {
            var archivedTrack = modelBuilder.Entity<ArchivedTrack>();
            archivedTrack.Property(t => t.Id)
                .ValueGeneratedOnAdd();

            archivedTrack
                .ToTable("ArchivedTracks")
                .HasKey(t => t.Id);

            archivedTrack
                .HasOne(x => x.RequestedByUser)
                .WithMany(x => x.RequestedArchivedTracks)
                .HasForeignKey(x => x.RequestedByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            archivedTrack
                .HasOne(x => x.Guild)
                .WithMany()
                .HasForeignKey(x => x.GuildId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        /* Archived Messages */ {
            var archivedMessage = modelBuilder.Entity<ArchivedMessage>();
            archivedMessage.ToTable("ArchivedMessages").HasKey(x => x.Id);
            archivedMessage.Property(x => x.Id).ValueGeneratedOnAdd();
            archivedMessage
                .HasOne(x => x.AuthorUser)
                .WithMany(x => x.AuthoredArchivedMessages)
                .HasForeignKey(x => x.AuthorUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
            archivedMessage
                .HasOne(x => x.Guild)
                .WithMany()
                .HasForeignKey(x => x.GuildId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        /*Guild Queue Item*/ {
            modelBuilder.Entity<GuildQueueItem>()
                .Property(t => t.Id)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<GuildQueueItem>()
                .HasOne(qi => qi.RequestedByUser)
                    .WithMany(usr => usr.RequestedSongs)
                .HasForeignKey(qi => qi.RequestedByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<GuildQueueItem>()
                .HasOne(qi => qi.DeletedByUser)
                    .WithMany(usr => usr.DeletedSongs)
                .HasForeignKey(qi => qi.DeletedByUserId)
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

            // Indexes

            modelBuilder.Entity<GuildQueueItem>()
                .HasIndex(x => new { x.GuildId, x.Position })
                .IsUnique();
        }

        /*Track Playlist Position*/ {
            var tppEnt = modelBuilder.Entity<TrackPlaylistPosition>();
            tppEnt.ToTable("TrackPlaylistPositions");
            tppEnt.HasKey(x => x.Id);
            tppEnt.Property(x => x.Id)
                .ValueGeneratedOnAdd();

            tppEnt.HasIndex(x => new { x.TrackSource, x.TrackSourceId, x.Position })
                .IsUnique()
                .HasDatabaseName(
                    "UX_TrackPlaylistPositions_Source_SourceId_Position");
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
        var changedEvents = ChangeTracker
            .Entries<GuildEventRecord>()
            .Where(x => x.State is EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (changedEvents.Count > 0)
            throw new InvalidOperationException("Guild events are append-only and cannot be modified or deleted.");

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

    public async Task<Guild> GetOrCreateDiscordGuild(DiscordGuild guild, CancellationToken cancellationToken = default) {
        Guild dbGuild;
        
        var query = Guilds
            .Where(x => x.Id == guild.Id);

        // if (includeTracks) query = query.Include(x => x.Tracks);

        if (await query.AsNoTracking().AnyAsync(cancellationToken) == false) {
            dbGuild = new Guild() {
                Id = guild.Id,
                Name = guild.Name,
                IsPlaying = false,
                TrackCount = 0
            };

            Guilds.Add(dbGuild);
            await SaveChangesAsync(cancellationToken);
        } else dbGuild = await query.FirstAsync(cancellationToken);

        return dbGuild;
    }

    public async Task<Guild?> GetGuild(ulong guildId) {
        var query = Guilds
            .Where(x => x.Id == guildId);

        if (await query.AnyAsync() == false) 
            return null;

        return await query.FirstAsync();
    }

    public async Task<string> GetUserDisplayNameForGuildAsync(
        TavernUser user,
        ulong guildId,
        CancellationToken cancellationToken = default
    ) {
        var guildUsers = this.Entry(user)
            .Collection(x => x.GuildUsers);

        // Check if our GuildUsers collection is loaded
        if (guildUsers.IsLoaded) {
            return user.GuildUsers.FirstOrDefault(x => x.GuildId == guildId)?.DisplayName
                ?? user.Username;
        }

        // If its not loaded lets fall back to load the username
        return await this.GuildUsers
            .Where(x => x.GuildId == guildId && x.UserId == user.Id)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken)
            ?? user.Username;
    }

    public async Task<ResolvedGuildUser> ResolveGuildUserAsync(
        DiscordMember member,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(member);

        var user = Users.Local.FirstOrDefault(x => x.Id == member.Id)
            ?? await Users.SingleOrDefaultAsync(x => x.Id == member.Id, cancellationToken);

        if (user == null) {
            user = new TavernUser {
                Id = member.Id,
                Username = member.Username
            };

            Users.Add(user);
        } else if (user.Username != member.Username) {
            user.Username = member.Username;
        }

        var guildId = member.Guild.Id;
        var guildUser = GuildUsers.Local
            .FirstOrDefault(x => x.GuildId == guildId && x.UserId == member.Id)
            ?? await GuildUsers
                .SingleOrDefaultAsync(
                    x => x.GuildId == guildId && x.UserId == member.Id,
                    cancellationToken);

        if (guildUser == null) {
            guildUser = new GuildUser {
                GuildId = guildId,
                UserId = member.Id,
                User = user,
                DisplayName = member.DisplayName
            };

            GuildUsers.Add(guildUser);
        } else if (guildUser.DisplayName != member.DisplayName) {
            guildUser.DisplayName = member.DisplayName;
        }

        return new ResolvedGuildUser(user, guildUser);
    }


    #endregion

}
