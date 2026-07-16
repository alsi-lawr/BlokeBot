using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _announcementOccurrenceStatuses =
        PersistedEnumTokens<AnnouncementOccurrenceStatus>.Values.ToArray();

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
        modelBuilder.Entity<CustomAnnouncement>(b =>
        {
            b.ToTable(
                "custom_announcements",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_custom_announcements_OccurrenceStatus",
                        KindIn("OccurrenceStatus", _announcementOccurrenceStatuses)
                    );
                    t.HasCheckConstraint(
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
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.OccurrenceStatus)
                .HasConversion(
                    value => PersistedEnumTokens<AnnouncementOccurrenceStatus>.Format(value),
                    value => PersistedEnumTokens<AnnouncementOccurrenceStatus>.Parse(value)
                )
                .HasMaxLength(40);
            b.Property(x => x.OccurrenceMessage).HasMaxLength(500);
            b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.MessageLibraryEntry)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.MessageLibraryEntryId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Schedule)
                .WithOne(x => x.Announcement)
                .HasForeignKey<CustomAnnouncementSchedule>(x => new
                {
                    x.HostId,
                    x.CustomAnnouncementId,
                })
                .HasPrincipalKey<CustomAnnouncement>(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.DeliveryPolicy)
                .WithOne(x => x.Announcement)
                .HasForeignKey<CustomAnnouncement>(x => new { x.HostId, x.DeliveryPolicyId })
                .HasPrincipalKey<CustomAnnouncementDeliveryPolicy>(x => new { x.HostId, x.Id })
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
            b.Navigation(x => x.DeliveryPolicy).IsRequired();
        });

        modelBuilder.Entity<CustomAnnouncementDeliveryPolicy>(b =>
        {
            b.ToTable(
                "custom_announcement_delivery_policies",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_custom_announcement_delivery_policies_PolicyType",
                        KindIn("PolicyType", _customAnnouncementDeliveryPolicyTypes)
                    );
                    t.HasCheckConstraint(
                        "CK_custom_announcement_delivery_policies_Payload",
                        "PolicyType = 'RetryUntilExpiredThenSkip' "
                            + "AND RetryDelayTicks IS NOT NULL AND RetryDelayTicks > 0 "
                            + "AND OccurrenceLifetimeTicks IS NOT NULL "
                            + $"AND OccurrenceLifetimeTicks <= {TimeSpan.FromSeconds(60).Ticks} "
                            + "AND RetryDelayTicks < OccurrenceLifetimeTicks"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.Property<CustomAnnouncementDeliveryPolicyKind>("PolicyType")
                .HasConversion<string>()
                .HasMaxLength(48);
            b.HasDiscriminator<CustomAnnouncementDeliveryPolicyKind>("PolicyType")
                .HasValue<RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy>(
                    CustomAnnouncementDeliveryPolicyKind.RetryUntilExpiredThenSkip
                );
        });

        modelBuilder.Entity<RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy>(b =>
        {
            b.Property(x => x.RetryDelay)
                .HasConversion(
                    value => value.Value.Ticks,
                    value => new AnnouncementRetryDelay(TimeSpan.FromTicks(value))
                )
                .HasColumnName("RetryDelayTicks");
            b.Property(x => x.OccurrenceLifetime)
                .HasConversion(
                    value => value.Value.Ticks,
                    value => new AnnouncementOccurrenceLifetime(TimeSpan.FromTicks(value))
                )
                .HasColumnName("OccurrenceLifetimeTicks");
        });

        modelBuilder.Entity<CustomAnnouncementSchedule>(b =>
        {
            b.ToTable(
                "custom_announcement_schedules",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_custom_announcement_schedules_ScheduleType",
                        KindIn("ScheduleType", _customAnnouncementScheduleTypes)
                    );
                    t.HasCheckConstraint(
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
            b.HasKey(x => x.CustomAnnouncementId);
            b.Property<string>("ScheduleType").HasMaxLength(32);
            b.HasDiscriminator<string>("ScheduleType")
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

        modelBuilder.Entity<IntervalCustomAnnouncementSchedule>(b =>
            b.Property(x => x.IntervalMinutes).HasColumnName("IntervalMinutes")
        );
        modelBuilder.Entity<IntervalAfterChatCustomAnnouncementSchedule>(b =>
        {
            b.Property(x => x.IntervalMinutes).HasColumnName("IntervalMinutes");
            b.Property(x => x.RequiredChatMessages).HasColumnName("RequiredChatMessages");
        });
        modelBuilder.Entity<WeeklyCustomAnnouncementSchedule>(b =>
        {
            b.Property(x => x.Day).HasColumnName("WeeklyDay");
            b.Property(x => x.Time).HasColumnName("WeeklyTime");
        });
    }
}
