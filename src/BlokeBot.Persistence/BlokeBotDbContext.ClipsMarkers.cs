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
        _ = modelBuilder.Entity<TwitchClip>(b =>
        {
            _ = b.ToTable(
                "twitch_clips",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_clips_Status",
                        KindIn("Status", _twitchClipStatusKinds)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdempotencyKey).HasMaxLength(128);
            _ = b.Property(x => x.ProviderClipId).HasMaxLength(128);
            _ = b.Property(x => x.EditUrl).HasMaxLength(1024);
            _ = b.Property(x => x.FinalUrl).HasMaxLength(1024);
            _ = b.Property(x => x.BroadcasterTwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.BroadcasterLogin).HasMaxLength(128);
            _ = b.Property(x => x.CreatorTwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.CreatorLogin).HasMaxLength(128);
            _ = b.Property(x => x.VideoId).HasMaxLength(128);
            _ = b.Property(x => x.FailureReason).HasMaxLength(256);
            _ = b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchClipStatus>.Format(status),
                    value => PersistedEnumTokens<TwitchClipStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new { x.HostId, x.IdempotencyKey }).IsUnique();
            _ = b.HasIndex(x => new
            {
                x.HostId,
                x.Status,
                x.ResolvedAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<TwitchStreamMarker>(b =>
        {
            _ = b.ToTable(
                "twitch_stream_markers",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_stream_markers_Status",
                        KindIn("Status", _twitchStreamMarkerStatusKinds)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdempotencyKey).HasMaxLength(128);
            _ = b.Property(x => x.ProviderMarkerId).HasMaxLength(128);
            _ = b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchStreamMarkerStatus>.Format(status),
                    value => PersistedEnumTokens<TwitchStreamMarkerStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.Description).HasMaxLength(140);
            _ = b.Property(x => x.MarkerUrl).HasMaxLength(1024);
            _ = b.Property(x => x.VideoId).HasMaxLength(128);
            _ = b.Property(x => x.FailureReason).HasMaxLength(256);
            _ = b.HasIndex(x => new { x.HostId, x.IdempotencyKey }).IsUnique();
            _ = b.HasIndex(x => new { x.HostId, x.CreatedAtUtc });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
