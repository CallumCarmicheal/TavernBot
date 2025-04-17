﻿// <auto-generated />
using System;
using CCTavern.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace CCTavern.Migrations
{
    [DbContext(typeof(TavernContext))]
    [Migration("20241220163858_CreateTrackPlaylistPositions")]
    partial class CreateTrackPlaylistPositions
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.3")
                .HasAnnotation("Relational:MaxIdentifierLength", 64);

            modelBuilder.Entity("CCTavern.Database.ArchivedMessage", b =>
                {
                    b.Property<ulong>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint unsigned");

                    b.Property<ulong?>("AuthorId")
                        .HasColumnType("bigint unsigned");

                    b.Property<bool>("ContainsPrefix")
                        .HasColumnType("tinyint(1)");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<DateTime>("DateMessageCreated")
                        .HasColumnType("datetime(6)");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("bigint unsigned");

                    b.Property<string>("MessageContents")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<ulong>("MessageId")
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime?>("UpdatedAt")
                        .HasColumnType("datetime(6)");

                    b.HasKey("Id");

                    b.HasIndex("AuthorId");

                    b.HasIndex("GuildId");

                    b.ToTable("ArchivedMessages");
                });

            modelBuilder.Entity("CCTavern.Database.ArchivedTrack", b =>
                {
                    b.Property<ulong>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<DateTime>("DateMessageCreated")
                        .HasColumnType("datetime(6)");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("bigint unsigned");

                    b.Property<TimeSpan>("Length")
                        .HasColumnType("time(6)");

                    b.Property<ulong>("MessageId")
                        .HasColumnType("bigint unsigned");

                    b.Property<ulong>("Position")
                        .HasColumnType("bigint unsigned");

                    b.Property<ulong?>("RequestedById")
                        .HasColumnType("bigint unsigned");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<string>("TrackString")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<DateTime?>("UpdatedAt")
                        .HasColumnType("datetime(6)");

                    b.HasKey("Id");

                    b.HasIndex("GuildId");

                    b.HasIndex("RequestedById");

                    b.ToTable("ArchivedTracks", (string)null);
                });

            modelBuilder.Entity("CCTavern.Database.CachedUser", b =>
                {
                    b.Property<ulong>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<string>("DisplayName")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime?>("UpdatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<ulong>("UserId")
                        .HasColumnType("bigint unsigned");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.HasKey("Id");

                    b.HasIndex("GuildId");

                    b.ToTable("CachedUsers", (string)null);
                });

            modelBuilder.Entity("CCTavern.Database.Guild", b =>
                {
                    b.Property<ulong>("Id")
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<ulong>("CurrentTrack")
                        .HasColumnType("bigint unsigned");

                    b.Property<bool>("IsPlaying")
                        .HasColumnType("tinyint(1)");

                    b.Property<ulong?>("LastMessageStatusId")
                        .HasColumnType("bigint unsigned");

                    b.Property<bool>("LeaveAfterQueue")
                        .HasColumnType("tinyint(1)");

                    b.Property<ulong?>("MusicChannelId")
                        .HasColumnType("bigint unsigned");

                    b.Property<string>("MusicChannelName")
                        .HasColumnType("longtext");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<ulong>("NextTrack")
                        .HasColumnType("bigint unsigned");

                    b.Property<string>("Prefixes")
                        .HasColumnType("longtext");

                    b.Property<ulong>("TrackCount")
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime?>("UpdatedAt")
                        .HasColumnType("datetime(6)");

                    b.HasKey("Id");

                    b.ToTable("Guilds", (string)null);
                });

            modelBuilder.Entity("CCTavern.Database.GuildQueueItem", b =>
                {
                    b.Property<ulong>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<DateTime?>("DateDeleted")
                        .HasColumnType("datetime(6)");

                    b.Property<ulong?>("DeletedById")
                        .HasColumnType("bigint unsigned");

                    b.Property<string>("DeletedReason")
                        .HasColumnType("longtext");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("bigint unsigned");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("tinyint(1)");

                    b.Property<TimeSpan>("Length")
                        .HasColumnType("time(6)");

                    b.Property<ulong?>("PlaylistId")
                        .HasColumnType("bigint unsigned");

                    b.Property<ulong>("Position")
                        .HasColumnType("bigint unsigned");

                    b.Property<ulong?>("RequestedById")
                        .HasColumnType("bigint unsigned");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<string>("TrackString")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<DateTime?>("UpdatedAt")
                        .HasColumnType("datetime(6)");

                    b.HasKey("Id");

                    b.HasIndex("DeletedById");

                    b.HasIndex("GuildId");

                    b.HasIndex("PlaylistId");

                    b.HasIndex("RequestedById");

                    b.ToTable("GuildQueueItems", (string)null);
                });

            modelBuilder.Entity("CCTavern.Database.GuildQueuePlaylist", b =>
                {
                    b.Property<ulong>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<ulong?>("CreatedById")
                        .HasColumnType("bigint unsigned");

                    b.Property<int>("PlaylistSongCount")
                        .HasColumnType("int");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<DateTime?>("UpdatedAt")
                        .HasColumnType("datetime(6)");

                    b.HasKey("Id");

                    b.HasIndex("CreatedById");

                    b.ToTable("GuildQueuePlaylists", (string)null);
                });

            modelBuilder.Entity("CCTavern.Database.TrackPlaylistPositions", b =>
                {
                    b.Property<ulong>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint unsigned");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<string>("DisplayText")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<TimeSpan>("Position")
                        .HasColumnType("time(6)");

                    b.Property<string>("TrackSource")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<string>("TrackSourceId")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<DateTime?>("UpdatedAt")
                        .HasColumnType("datetime(6)");

                    b.HasKey("Id");

                    b.ToTable("TrackPlaylistPositions");
                });

            modelBuilder.Entity("CCTavern.Database.ArchivedMessage", b =>
                {
                    b.HasOne("CCTavern.Database.CachedUser", "Author")
                        .WithMany()
                        .HasForeignKey("AuthorId");

                    b.HasOne("CCTavern.Database.Guild", "Guild")
                        .WithMany()
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Author");

                    b.Navigation("Guild");
                });

            modelBuilder.Entity("CCTavern.Database.ArchivedTrack", b =>
                {
                    b.HasOne("CCTavern.Database.Guild", "Guild")
                        .WithMany()
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("CCTavern.Database.CachedUser", "RequestedBy")
                        .WithMany("ArchivedTracks")
                        .HasForeignKey("RequestedById");

                    b.Navigation("Guild");

                    b.Navigation("RequestedBy");
                });

            modelBuilder.Entity("CCTavern.Database.CachedUser", b =>
                {
                    b.HasOne("CCTavern.Database.Guild", "Guild")
                        .WithMany()
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Guild");
                });

            modelBuilder.Entity("CCTavern.Database.GuildQueueItem", b =>
                {
                    b.HasOne("CCTavern.Database.CachedUser", "DeletedBy")
                        .WithMany("DeletedSongs")
                        .HasForeignKey("DeletedById")
                        .OnDelete(DeleteBehavior.SetNull);

                    b.HasOne("CCTavern.Database.Guild", "Guild")
                        .WithMany("Tracks")
                        .HasForeignKey("GuildId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("CCTavern.Database.GuildQueuePlaylist", "Playlist")
                        .WithMany("Songs")
                        .HasForeignKey("PlaylistId")
                        .OnDelete(DeleteBehavior.SetNull);

                    b.HasOne("CCTavern.Database.CachedUser", "RequestedBy")
                        .WithMany("RequestedSongs")
                        .HasForeignKey("RequestedById")
                        .OnDelete(DeleteBehavior.SetNull);

                    b.Navigation("DeletedBy");

                    b.Navigation("Guild");

                    b.Navigation("Playlist");

                    b.Navigation("RequestedBy");
                });

            modelBuilder.Entity("CCTavern.Database.GuildQueuePlaylist", b =>
                {
                    b.HasOne("CCTavern.Database.CachedUser", "CreatedBy")
                        .WithMany()
                        .HasForeignKey("CreatedById");

                    b.Navigation("CreatedBy");
                });

            modelBuilder.Entity("CCTavern.Database.CachedUser", b =>
                {
                    b.Navigation("ArchivedTracks");

                    b.Navigation("DeletedSongs");

                    b.Navigation("RequestedSongs");
                });

            modelBuilder.Entity("CCTavern.Database.Guild", b =>
                {
                    b.Navigation("Tracks");
                });

            modelBuilder.Entity("CCTavern.Database.GuildQueuePlaylist", b =>
                {
                    b.Navigation("Songs");
                });
#pragma warning restore 612, 618
        }
    }
}
