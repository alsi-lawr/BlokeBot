using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _announcementOccurrenceStatuses =
        PersistedEnumTokens<AnnouncementOccurrenceStatus>.Values.ToArray();

    private static readonly string[] _customAnnouncementDeliveryTypes =
        PersistedEnumTokens<CustomAnnouncementDeliveryType>.Values.ToArray();

    private static readonly string[] _twitchAnnouncementColors =
        PersistedEnumTokens<TwitchAnnouncementColor>.Values.ToArray();

    private static readonly string[] _customAnnouncementLatestDeliveryResults =
        PersistedEnumTokens<CustomAnnouncementLatestDeliveryResult>.Values.ToArray();

    private static readonly string[] _customAnnouncementScheduleTypes =
    [
        IntervalCustomAnnouncementSchedule.Discriminator,
        IntervalAfterChatCustomAnnouncementSchedule.Discriminator,
        WeeklyCustomAnnouncementSchedule.Discriminator,
    ];

    private static readonly string[] _customAnnouncementDeliveryPolicyTypes =
    [
        nameof(CustomAnnouncementDeliveryPolicyKind.RetryUntilExpiredThenSkip),
    ];

    private static void ConfigureAnnouncements(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CustomAnnouncement>(b =>
        {
            _ = b.ToTable(
                "custom_announcements",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_OccurrenceStatus",
                        KindIn("OccurrenceStatus", _announcementOccurrenceStatuses)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_DeliveryType",
                        KindIn("DeliveryType", _customAnnouncementDeliveryTypes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_AnnouncementColor",
                        KindIn("AnnouncementColor", _twitchAnnouncementColors)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_LatestDeliveryResult",
                        KindIn("LatestDeliveryResult", _customAnnouncementLatestDeliveryResults)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_OccurrenceState",
                        "(OccurrenceStatus = 'None' AND OccurrenceDueAtUtc IS NULL "
                            + "AND OccurrenceExpiresAtUtc IS NULL AND OccurrenceNextAttemptAtUtc IS NULL "
                            + "AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount = 0 "
                            + "AND OccurrenceMessage IS NULL) OR "
                            + "(OccurrenceStatus = 'Pending' AND OccurrenceDueAtUtc IS NOT NULL "
                            + "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc "
                            + "AND OccurrenceNextAttemptAtUtc IS NOT NULL "
                            + "AND OccurrenceNextAttemptAtUtc <= OccurrenceExpiresAtUtc "
                            + "AND OccurrenceCompletedAtUtc IS NULL "
                            + "AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR "
                            + "(OccurrenceStatus = 'Attempting' AND OccurrenceDueAtUtc IS NOT NULL "
                            + "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc "
                            + "AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NULL "
                            + "AND OccurrenceAttemptCount > 0 AND length(OccurrenceMessage) > 0) OR "
                            + "(OccurrenceStatus = 'RetryScheduled' AND OccurrenceDueAtUtc IS NOT NULL "
                            + "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc "
                            + "AND OccurrenceNextAttemptAtUtc >= OccurrenceDueAtUtc "
                            + "AND OccurrenceNextAttemptAtUtc <= OccurrenceExpiresAtUtc "
                            + "AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount > 0 "
                            + "AND length(OccurrenceMessage) > 0) OR "
                            + "(OccurrenceStatus IN ('Accepted', 'TerminalRejected', "
                            + "'TerminalAmbiguous', 'TerminalUnexpected') "
                            + "AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc "
                            + "AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL "
                            + "AND OccurrenceAttemptCount > 0 AND OccurrenceMessage IS NULL) OR "
                            + "(OccurrenceStatus = 'SkippedExpired' AND OccurrenceDueAtUtc IS NOT NULL "
                            + "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc "
                            + "AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL "
                            + "AND OccurrenceAttemptCount >= 0 AND OccurrenceMessage IS NULL) OR "
                            + "(OccurrenceStatus = 'TerminalMissingMessage' AND OccurrenceDueAtUtc IS NOT NULL "
                            + "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc "
                            + "AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL "
                            + "AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR "
                            + "(OccurrenceStatus = 'TerminalInvalidTimeZone' AND OccurrenceDueAtUtc IS NULL "
                            + "AND OccurrenceExpiresAtUtc IS NULL AND OccurrenceNextAttemptAtUtc IS NULL "
                            + "AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount = 0 "
                            + "AND OccurrenceMessage IS NULL)"
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.Name).HasMaxLength(128);
            _ = b.Property(x => x.OccurrenceStatus)
                .HasConversion(
                    value => PersistedEnumTokens<AnnouncementOccurrenceStatus>.Format(value),
                    value => PersistedEnumTokens<AnnouncementOccurrenceStatus>.Parse(value)
                )
                .HasMaxLength(40);
            _ = b.Property(x => x.DeliveryType)
                .HasConversion(
                    value => PersistedEnumTokens<CustomAnnouncementDeliveryType>.Format(value),
                    value => PersistedEnumTokens<CustomAnnouncementDeliveryType>.Parse(value)
                )
                .HasMaxLength(32)
                .HasDefaultValue(CustomAnnouncementDeliveryType.ChatMessage);
            _ = b.Property(x => x.AnnouncementColor)
                .HasConversion(
                    value => PersistedEnumTokens<TwitchAnnouncementColor>.Format(value),
                    value => PersistedEnumTokens<TwitchAnnouncementColor>.Parse(value)
                )
                .HasMaxLength(16)
                .HasDefaultValue(TwitchAnnouncementColor.Primary);
            _ = b.Property(x => x.LatestDeliveryResult)
                .HasConversion(
                    value =>
                        PersistedEnumTokens<CustomAnnouncementLatestDeliveryResult>.Format(value),
                    value =>
                        PersistedEnumTokens<CustomAnnouncementLatestDeliveryResult>.Parse(value)
                )
                .HasMaxLength(20)
                .HasDefaultValue(CustomAnnouncementLatestDeliveryResult.None);
            _ = b.Property(x => x.OccurrenceMessage).HasMaxLength(500);
            _ = b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(x => x.MessageLibraryEntry)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.MessageLibraryEntryId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(x => x.Schedule)
                .WithOne(x => x.Announcement)
                .HasForeignKey<CustomAnnouncementSchedule>(x => new
                {
                    x.HostId,
                    x.CustomAnnouncementId,
                })
                .HasPrincipalKey<CustomAnnouncement>(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(x => x.DeliveryPolicy)
                .WithOne(x => x.Announcement)
                .HasForeignKey<CustomAnnouncement>(x => new { x.HostId, x.DeliveryPolicyId })
                .HasPrincipalKey<CustomAnnouncementDeliveryPolicy>(x => new { x.HostId, x.Id })
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.Navigation(x => x.DeliveryPolicy).IsRequired();
        });

        _ = modelBuilder.Entity<CustomAnnouncementDeliveryPolicy>(b =>
        {
            _ = b.ToTable(
                "custom_announcement_delivery_policies",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcement_delivery_policies_PolicyType",
                        KindIn("PolicyType", _customAnnouncementDeliveryPolicyTypes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcement_delivery_policies_Payload",
                        "PolicyType = 'RetryUntilExpiredThenSkip' "
                            + "AND RetryDelayTicks IS NOT NULL AND RetryDelayTicks > 0 "
                            + "AND OccurrenceLifetimeTicks IS NOT NULL "
                            + $"AND OccurrenceLifetimeTicks <= {TimeSpan.FromSeconds(60).Ticks} "
                            + "AND RetryDelayTicks < OccurrenceLifetimeTicks"
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.Property<CustomAnnouncementDeliveryPolicyKind>("PolicyType")
                .HasConversion<string>()
                .HasMaxLength(48);
            _ = b.HasDiscriminator<CustomAnnouncementDeliveryPolicyKind>("PolicyType")
                .HasValue<RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy>(
                    CustomAnnouncementDeliveryPolicyKind.RetryUntilExpiredThenSkip
                );
        });

        _ = modelBuilder.Entity<RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy>(b =>
        {
            _ = b.Property(x => x.RetryDelay)
                .HasConversion(
                    value => value.Value.Ticks,
                    value => new AnnouncementRetryDelay(TimeSpan.FromTicks(value))
                )
                .HasColumnName("RetryDelayTicks");
            _ = b.Property(x => x.OccurrenceLifetime)
                .HasConversion(
                    value => value.Value.Ticks,
                    value => new AnnouncementOccurrenceLifetime(TimeSpan.FromTicks(value))
                )
                .HasColumnName("OccurrenceLifetimeTicks");
        });

        _ = modelBuilder.Entity<CustomAnnouncementSchedule>(b =>
        {
            _ = b.ToTable(
                "custom_announcement_schedules",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcement_schedules_ScheduleType",
                        KindIn("ScheduleType", _customAnnouncementScheduleTypes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcement_schedules_Payload",
                        "(ScheduleType = 'Interval' AND IntervalMinutes >= 1 "
                            + "AND RequiredChatMessages IS NULL AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR "
                            + "(ScheduleType = 'IntervalAfterChat' AND IntervalMinutes >= 1 "
                            + "AND RequiredChatMessages >= 1 AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR "
                            + "(ScheduleType = 'Weekly' AND IntervalMinutes IS NULL "
                            + "AND RequiredChatMessages IS NULL AND WeeklyDay BETWEEN 0 AND 6 "
                            + "AND WeeklyTime IS NOT NULL)"
                    );
                }
            );
            _ = b.HasKey(x => x.CustomAnnouncementId);
            _ = b.Property<string>("ScheduleType").HasMaxLength(32);
            _ = b.HasDiscriminator<string>("ScheduleType")
                .HasValue<IntervalCustomAnnouncementSchedule>(
                    IntervalCustomAnnouncementSchedule.Discriminator
                )
                .HasValue<IntervalAfterChatCustomAnnouncementSchedule>(
                    IntervalAfterChatCustomAnnouncementSchedule.Discriminator
                )
                .HasValue<WeeklyCustomAnnouncementSchedule>(
                    WeeklyCustomAnnouncementSchedule.Discriminator
                );
        });

        _ = modelBuilder.Entity<IntervalCustomAnnouncementSchedule>(b =>
            b.Property(x => x.IntervalMinutes).HasColumnName("IntervalMinutes")
        );
        _ = modelBuilder.Entity<IntervalAfterChatCustomAnnouncementSchedule>(b =>
        {
            _ = b.Property(x => x.IntervalMinutes).HasColumnName("IntervalMinutes");
            _ = b.Property(x => x.RequiredChatMessages).HasColumnName("RequiredChatMessages");
        });
        _ = modelBuilder.Entity<WeeklyCustomAnnouncementSchedule>(b =>
        {
            _ = b.Property(x => x.Day).HasColumnName("WeeklyDay");
            _ = b.Property(x => x.Time).HasColumnName("WeeklyTime");
        });
    }
}
