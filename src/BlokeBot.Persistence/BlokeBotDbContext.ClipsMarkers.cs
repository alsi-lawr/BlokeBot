using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _twitchClipStatusKinds =
        PersistedEnumTokens<TwitchClipStatus>.Values.ToArray();

    private static readonly string[] _twitchStreamMarkerStatusKinds =
        PersistedEnumTokens<TwitchStreamMarkerStatus>.Values.ToArray();

    private static void ConfigureClipsMarkers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TwitchClip>(b =>
        {
            b.ToTable(
                "twitch_clips",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_clips_Status",
                        KindIn("Status", _twitchClipStatusKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.IdempotencyKey).HasMaxLength(128);
            b.Property(x => x.ProviderClipId).HasMaxLength(128);
            b.Property(x => x.EditUrl).HasMaxLength(1024);
            b.Property(x => x.FinalUrl).HasMaxLength(1024);
            b.Property(x => x.BroadcasterTwitchUserId).HasMaxLength(64);
            b.Property(x => x.BroadcasterLogin).HasMaxLength(128);
            b.Property(x => x.CreatorTwitchUserId).HasMaxLength(64);
            b.Property(x => x.CreatorLogin).HasMaxLength(128);
            b.Property(x => x.VideoId).HasMaxLength(128);
            b.Property(x => x.FailureReason).HasMaxLength(256);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchClipStatus>.Format(status),
                    value => PersistedEnumTokens<TwitchClipStatus>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => new
            {
                x.HostId,
                x.Status,
                x.ResolvedAtUtc,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TwitchStreamMarker>(b =>
        {
            b.ToTable(
                "twitch_stream_markers",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_stream_markers_Status",
                        KindIn("Status", _twitchStreamMarkerStatusKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.IdempotencyKey).HasMaxLength(128);
            b.Property(x => x.ProviderMarkerId).HasMaxLength(128);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchStreamMarkerStatus>.Format(status),
                    value => PersistedEnumTokens<TwitchStreamMarkerStatus>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.Description).HasMaxLength(140);
            b.Property(x => x.MarkerUrl).HasMaxLength(1024);
            b.Property(x => x.VideoId).HasMaxLength(128);
            b.Property(x => x.FailureReason).HasMaxLength(256);
            b.HasIndex(x => new { x.HostId, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => new { x.HostId, x.CreatedAtUtc });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
