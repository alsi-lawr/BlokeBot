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
        _ = modelBuilder.Entity<CustomAnnouncement>(static b =>
        {
            _ = b.ToTable(
                "custom_announcements",
                static t =>
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
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.Property(static x => x.OccurrenceStatus)
                .HasConversion(
                    static value => PersistedEnumTokens<AnnouncementOccurrenceStatus>.Format(value),
                    static value => PersistedEnumTokens<AnnouncementOccurrenceStatus>.Parse(value)
                )
                .HasMaxLength(40);
            _ = b.Property(static x => x.DeliveryType)
                .HasConversion(
                    static value =>
                        PersistedEnumTokens<CustomAnnouncementDeliveryType>.Format(value),
                    static value => PersistedEnumTokens<CustomAnnouncementDeliveryType>.Parse(value)
                )
                .HasMaxLength(32)
                .HasDefaultValue(CustomAnnouncementDeliveryType.ChatMessage);
            _ = b.Property(static x => x.AnnouncementColor)
                .HasConversion(
                    static value => PersistedEnumTokens<TwitchAnnouncementColor>.Format(value),
                    static value => PersistedEnumTokens<TwitchAnnouncementColor>.Parse(value)
                )
                .HasMaxLength(16)
                .HasDefaultValue(TwitchAnnouncementColor.Primary);
            _ = b.Property(static x => x.LatestDeliveryResult)
                .HasConversion(
                    static value =>
                        PersistedEnumTokens<CustomAnnouncementLatestDeliveryResult>.Format(value),
                    static value =>
                        PersistedEnumTokens<CustomAnnouncementLatestDeliveryResult>.Parse(value)
                )
                .HasMaxLength(20)
                .HasDefaultValue(CustomAnnouncementLatestDeliveryResult.None);
            _ = b.Property(static x => x.OccurrenceMessage).HasMaxLength(500);
            _ = b.HasIndex(static x => new { x.HostId, x.Name }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.MessageLibraryEntry)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.MessageLibraryEntryId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(static x => x.Schedule)
                .WithOne(static x => x.Announcement)
                .HasForeignKey<CustomAnnouncementSchedule>(static x => new
                {
                    x.HostId,
                    x.CustomAnnouncementId,
                })
                .HasPrincipalKey<CustomAnnouncement>(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.DeliveryPolicy)
                .WithOne(static x => x.Announcement)
                .HasForeignKey<CustomAnnouncement>(static x => new { x.HostId, x.DeliveryPolicyId })
                .HasPrincipalKey<CustomAnnouncementDeliveryPolicy>(static x => new
                {
                    x.HostId,
                    x.Id,
                })
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.Navigation(static x => x.DeliveryPolicy).IsRequired();
        });

        _ = modelBuilder.Entity<CustomAnnouncementDeliveryPolicy>(static b =>
        {
            _ = b.ToTable(
                "custom_announcement_delivery_policies",
                static t =>
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
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.Property<CustomAnnouncementDeliveryPolicyKind>("PolicyType")
                .HasConversion<string>()
                .HasMaxLength(48);
            _ = b.HasDiscriminator<CustomAnnouncementDeliveryPolicyKind>("PolicyType")
                .HasValue<RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy>(
                    CustomAnnouncementDeliveryPolicyKind.RetryUntilExpiredThenSkip
                );
        });

        _ = modelBuilder.Entity<RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy>(
            static b =>
            {
                _ = b.Property(static x => x.RetryDelay)
                    .HasConversion(
                        static value => value.Value.Ticks,
                        static value => new AnnouncementRetryDelay(TimeSpan.FromTicks(value))
                    )
                    .HasColumnName("RetryDelayTicks");
                _ = b.Property(static x => x.OccurrenceLifetime)
                    .HasConversion(
                        static value => value.Value.Ticks,
                        static value => new AnnouncementOccurrenceLifetime(
                            TimeSpan.FromTicks(value)
                        )
                    )
                    .HasColumnName("OccurrenceLifetimeTicks");
            }
        );

        _ = modelBuilder.Entity<CustomAnnouncementSchedule>(static b =>
        {
            _ = b.ToTable(
                "custom_announcement_schedules",
                static t =>
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
            _ = b.HasKey(static x => x.CustomAnnouncementId);
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

        _ = modelBuilder.Entity<IntervalCustomAnnouncementSchedule>(static b =>
            b.Property(static x => x.IntervalMinutes).HasColumnName("IntervalMinutes")
        );
        _ = modelBuilder.Entity<IntervalAfterChatCustomAnnouncementSchedule>(static b =>
        {
            _ = b.Property(static x => x.IntervalMinutes).HasColumnName("IntervalMinutes");
            _ = b.Property(static x => x.RequiredChatMessages)
                .HasColumnName("RequiredChatMessages");
        });
        _ = modelBuilder.Entity<WeeklyCustomAnnouncementSchedule>(static b =>
        {
            _ = b.Property(static x => x.Day).HasColumnName("WeeklyDay");
            _ = b.Property(static x => x.Time).HasColumnName("WeeklyTime");
        });
    }
}
