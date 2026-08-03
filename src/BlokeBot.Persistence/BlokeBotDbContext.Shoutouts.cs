using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _automaticRaidMechanisms =
        PersistedEnumTokens<AutomaticRaidShoutoutMechanism>.Values.ToArray();
    private static readonly string[] _automaticRaidChatPresentations =
        PersistedEnumTokens<AutomaticRaidChatPresentation>.Values.ToArray();
    private static readonly string[] _automaticRaidOutcomeStatuses =
        PersistedEnumTokens<AutomaticRaidShoutoutOutcomeStatus>.Values.ToArray();
    private static readonly string[] _automaticRaidResultCodes =
        PersistedEnumTokens<AutomaticRaidShoutoutResultCode>.Values.ToArray();

    private static void ConfigureShoutouts(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<ShoutoutHistoryEntry>(b =>
        {
            _ = b.ToTable("shoutout_history");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            _ = b.Property(x => x.SourceTwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.SourceLogin).HasMaxLength(128);
            _ = b.Property(x => x.TargetTwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.TargetLogin).HasMaxLength(128);
            _ = b.HasIndex(x => new { x.HostId, x.OccurredAtUtc });
            _ = b.HasIndex(x => new { x.HostId, x.ProviderMessageId }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<ShoutoutCooldownState>(b =>
        {
            _ = b.ToTable("shoutout_cooldowns");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.TargetTwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.TargetLogin).HasMaxLength(128);
            _ = b.HasIndex(x => new { x.HostId, x.TargetTwitchUserId }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<AutomaticRaidShoutoutSettings>(b =>
        {
            _ = b.ToTable(
                "automatic_raid_shoutout_settings",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_automatic_raid_shoutout_settings_MinimumViewerCount",
                        "MinimumViewerCount >= 1"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_automatic_raid_shoutout_settings_Mechanism",
                        KindIn("Mechanism", _automaticRaidMechanisms)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_automatic_raid_shoutout_settings_ChatPresentation",
                        KindIn("ChatPresentation", _automaticRaidChatPresentations)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_automatic_raid_shoutout_settings_AnnouncementColor",
                        KindIn("AnnouncementColor", _twitchAnnouncementColors)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_automatic_raid_shoutout_settings_PinDuration",
                        "PinDurationSeconds IS NULL OR (PinDurationSeconds >= 30 AND PinDurationSeconds <= 1800)"
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Enabled).HasDefaultValue(false);
            _ = b.Property(x => x.MinimumViewerCount).HasDefaultValue(1);
            _ = b.Property(x => x.Mechanism)
                .HasConversion(
                    v => PersistedEnumTokens<AutomaticRaidShoutoutMechanism>.Format(v),
                    v => PersistedEnumTokens<AutomaticRaidShoutoutMechanism>.Parse(v)
                )
                .HasMaxLength(16)
                .HasDefaultValue(AutomaticRaidShoutoutMechanism.Native);
            _ = b.Property(x => x.ChatPresentation)
                .HasConversion(
                    v => PersistedEnumTokens<AutomaticRaidChatPresentation>.Format(v),
                    v => PersistedEnumTokens<AutomaticRaidChatPresentation>.Parse(v)
                )
                .HasMaxLength(16)
                .HasDefaultValue(AutomaticRaidChatPresentation.Regular);
            _ = b.Property(x => x.MessageTemplate)
                .HasMaxLength(1024)
                .HasDefaultValue(AutomaticRaidShoutoutDefaults.MessageTemplate);
            _ = b.Property(x => x.AnnouncementColor)
                .HasConversion(
                    v => PersistedEnumTokens<TwitchAnnouncementColor>.Format(v),
                    v => PersistedEnumTokens<TwitchAnnouncementColor>.Parse(v)
                )
                .HasMaxLength(16)
                .HasDefaultValue(TwitchAnnouncementColor.Primary);
            _ = b.HasIndex(x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<AutomaticRaidProcessedEvent>(b =>
        {
            _ = b.ToTable(
                "automatic_raid_processed_events",
                t =>
                    t.HasCheckConstraint(
                        "CK_automatic_raid_processed_events_Expiry",
                        "ExpiresAtUtc >= ClaimedAtUtc"
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            _ = b.HasIndex(x => new { x.HostId, x.ProviderMessageId }).IsUnique();
            _ = b.HasIndex(x => x.ExpiresAtUtc);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<AutomaticRaidShoutoutOutcome>(b =>
        {
            _ = b.ToTable(
                "automatic_raid_shoutout_outcomes",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_Status",
                        KindIn("Status", _automaticRaidOutcomeStatuses)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_ResultCode",
                        KindInOrNull("ResultCode", _automaticRaidResultCodes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_State",
                        "(Status = 'Processing' AND ResultCode IS NULL AND CompletedAtUtc IS NULL) OR (Status = 'Delivered' AND ResultCode IS NOT NULL AND ResultCode = 'Delivered' AND CompletedAtUtc IS NOT NULL) OR (Status = 'NotDelivered' AND ResultCode IS NOT NULL AND ResultCode NOT IN ('Delivered', 'Ambiguous') AND CompletedAtUtc IS NOT NULL) OR (Status = 'Ambiguous' AND ResultCode IS NOT NULL AND ResultCode = 'Ambiguous' AND CompletedAtUtc IS NOT NULL)"
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            _ = b.Property(x => x.SourceTwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.SourceLogin).HasMaxLength(128);
            _ = b.Property(x => x.SourceDisplayName).HasMaxLength(128);
            _ = b.Property(x => x.Status)
                .HasConversion(
                    v => PersistedEnumTokens<AutomaticRaidShoutoutOutcomeStatus>.Format(v),
                    v => PersistedEnumTokens<AutomaticRaidShoutoutOutcomeStatus>.Parse(v)
                )
                .HasMaxLength(20);
            _ = b.Property(x => x.ResultCode)
                .HasConversion(
                    v =>
                        v.HasValue
                            ? PersistedEnumTokens<AutomaticRaidShoutoutResultCode>.Format(v.Value)
                            : null,
                    v =>
                        v == null
                            ? null
                            : PersistedEnumTokens<AutomaticRaidShoutoutResultCode>.Parse(v)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new { x.HostId, x.ProviderMessageId }).IsUnique();
            _ = b.HasIndex(x => new { x.HostId, x.CompletedAtUtc });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
