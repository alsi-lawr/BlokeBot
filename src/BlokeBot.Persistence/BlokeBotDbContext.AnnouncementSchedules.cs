using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureAnnouncementSchedules(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CustomAnnouncementDeliveryPolicy>(b =>
        {
            _ = b.ToTable(
                "custom_announcement_delivery_policies",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcement_delivery_policies_PolicyType",
                        KindIn(modelBuilder, "PolicyType", _customAnnouncementDeliveryPolicyTypes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcement_delivery_policies_Payload",
                        ProviderSql(
                            modelBuilder,
                            "PolicyType = 'RetryUntilExpiredThenSkip' ",
                            "\"PolicyType\" = 'RetryUntilExpiredThenSkip' "
                        )
                            + ProviderSql(
                                modelBuilder,
                                "AND RetryDelayTicks IS NOT NULL AND RetryDelayTicks > 0 ",
                                "AND \"RetryDelayTicks\" IS NOT NULL AND \"RetryDelayTicks\" > 0 "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceLifetimeTicks IS NOT NULL ",
                                "AND \"OccurrenceLifetimeTicks\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                $"AND OccurrenceLifetimeTicks <= {TimeSpan.FromSeconds(60).Ticks} ",
                                $"AND \"OccurrenceLifetimeTicks\" <= {TimeSpan.FromSeconds(60).Ticks} "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND RetryDelayTicks < OccurrenceLifetimeTicks",
                                "AND \"RetryDelayTicks\" < \"OccurrenceLifetimeTicks\""
                            )
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

        _ = modelBuilder.Entity<CustomAnnouncementSchedule>(b =>
        {
            _ = b.ToTable(
                "custom_announcement_schedules",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcement_schedules_ScheduleType",
                        KindIn(modelBuilder, "ScheduleType", _customAnnouncementScheduleTypes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcement_schedules_Payload",
                        ProviderSql(
                            modelBuilder,
                            "(ScheduleType = 'Interval' AND IntervalMinutes >= 1 ",
                            "(\"ScheduleType\" = 'Interval' AND \"IntervalMinutes\" >= 1 "
                        )
                            + ProviderSql(
                                modelBuilder,
                                "AND RequiredChatMessages IS NULL AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR ",
                                "AND \"RequiredChatMessages\" IS NULL AND \"WeeklyDay\" IS NULL AND \"WeeklyTime\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(ScheduleType = 'IntervalAfterChat' AND IntervalMinutes >= 1 ",
                                "(\"ScheduleType\" = 'IntervalAfterChat' AND \"IntervalMinutes\" >= 1 "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND RequiredChatMessages >= 1 AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR ",
                                "AND \"RequiredChatMessages\" >= 1 AND \"WeeklyDay\" IS NULL AND \"WeeklyTime\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(ScheduleType = 'Weekly' AND IntervalMinutes IS NULL ",
                                "(\"ScheduleType\" = 'Weekly' AND \"IntervalMinutes\" IS NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND RequiredChatMessages IS NULL AND WeeklyDay BETWEEN 0 AND 6 ",
                                "AND \"RequiredChatMessages\" IS NULL AND \"WeeklyDay\" BETWEEN 0 AND 6 "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND WeeklyTime IS NOT NULL)",
                                "AND \"WeeklyTime\" IS NOT NULL)"
                            )
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

        _ = modelBuilder.Entity<IntervalCustomAnnouncementSchedule>(b =>
            b.Property(static x => x.IntervalMinutes).HasColumnName("IntervalMinutes")
        );
        _ = modelBuilder.Entity<IntervalAfterChatCustomAnnouncementSchedule>(b =>
        {
            _ = b.Property(static x => x.IntervalMinutes).HasColumnName("IntervalMinutes");
            _ = b.Property(static x => x.RequiredChatMessages)
                .HasColumnName("RequiredChatMessages");
        });
        _ = modelBuilder.Entity<WeeklyCustomAnnouncementSchedule>(b =>
        {
            _ = b.Property(static x => x.Day).HasColumnName("WeeklyDay");
            _ = b.Property(static x => x.Time).HasColumnName("WeeklyTime");
        });
    }
}
