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
                        KindIn(modelBuilder, "OccurrenceStatus", _announcementOccurrenceStatuses)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_DeliveryType",
                        KindIn(modelBuilder, "DeliveryType", _customAnnouncementDeliveryTypes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_AnnouncementColor",
                        KindIn(modelBuilder, "AnnouncementColor", _twitchAnnouncementColors)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_LatestDeliveryResult",
                        KindIn(
                            modelBuilder,
                            "LatestDeliveryResult",
                            _customAnnouncementLatestDeliveryResults
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_announcements_OccurrenceState",
                        ProviderSql(
                            modelBuilder,
                            "(OccurrenceStatus = 'None' AND OccurrenceDueAtUtc IS NULL ",
                            "(\"OccurrenceStatus\" = 'None' AND \"OccurrenceDueAtUtc\" IS NULL "
                        )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceExpiresAtUtc IS NULL AND OccurrenceNextAttemptAtUtc IS NULL ",
                                "AND \"OccurrenceExpiresAtUtc\" IS NULL AND \"OccurrenceNextAttemptAtUtc\" IS NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount = 0 ",
                                "AND \"OccurrenceCompletedAtUtc\" IS NULL AND \"OccurrenceAttemptCount\" = 0 "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceMessage IS NULL) OR ",
                                "AND \"OccurrenceMessage\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(OccurrenceStatus = 'Pending' AND OccurrenceDueAtUtc IS NOT NULL ",
                                "(\"OccurrenceStatus\" = 'Pending' AND \"OccurrenceDueAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc ",
                                "AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceNextAttemptAtUtc IS NOT NULL ",
                                "AND \"OccurrenceNextAttemptAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceNextAttemptAtUtc <= OccurrenceExpiresAtUtc ",
                                "AND \"OccurrenceNextAttemptAtUtc\" <= \"OccurrenceExpiresAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceCompletedAtUtc IS NULL ",
                                "AND \"OccurrenceCompletedAtUtc\" IS NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR ",
                                "AND \"OccurrenceAttemptCount\" = 0 AND \"OccurrenceMessage\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(OccurrenceStatus = 'Attempting' AND OccurrenceDueAtUtc IS NOT NULL ",
                                "(\"OccurrenceStatus\" = 'Attempting' AND \"OccurrenceDueAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc ",
                                "AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NULL ",
                                "AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceAttemptCount > 0 AND length(OccurrenceMessage) > 0) OR ",
                                "AND \"OccurrenceAttemptCount\" > 0 AND length(\"OccurrenceMessage\") > 0) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(OccurrenceStatus = 'RetryScheduled' AND OccurrenceDueAtUtc IS NOT NULL ",
                                "(\"OccurrenceStatus\" = 'RetryScheduled' AND \"OccurrenceDueAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc ",
                                "AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceNextAttemptAtUtc >= OccurrenceDueAtUtc ",
                                "AND \"OccurrenceNextAttemptAtUtc\" >= \"OccurrenceDueAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceNextAttemptAtUtc <= OccurrenceExpiresAtUtc ",
                                "AND \"OccurrenceNextAttemptAtUtc\" <= \"OccurrenceExpiresAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount > 0 ",
                                "AND \"OccurrenceCompletedAtUtc\" IS NULL AND \"OccurrenceAttemptCount\" > 0 "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND length(OccurrenceMessage) > 0) OR ",
                                "AND length(\"OccurrenceMessage\") > 0) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(OccurrenceStatus IN ('Accepted', 'TerminalRejected', ",
                                "(\"OccurrenceStatus\" IN ('Accepted', 'TerminalRejected', "
                            )
                            + "'TerminalAmbiguous', 'TerminalUnexpected') "
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc ",
                                "AND \"OccurrenceDueAtUtc\" IS NOT NULL AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL ",
                                "AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceAttemptCount > 0 AND OccurrenceMessage IS NULL) OR ",
                                "AND \"OccurrenceAttemptCount\" > 0 AND \"OccurrenceMessage\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(OccurrenceStatus = 'SkippedExpired' AND OccurrenceDueAtUtc IS NOT NULL ",
                                "(\"OccurrenceStatus\" = 'SkippedExpired' AND \"OccurrenceDueAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc ",
                                "AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL ",
                                "AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceAttemptCount >= 0 AND OccurrenceMessage IS NULL) OR ",
                                "AND \"OccurrenceAttemptCount\" >= 0 AND \"OccurrenceMessage\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(OccurrenceStatus = 'TerminalMissingMessage' AND OccurrenceDueAtUtc IS NOT NULL ",
                                "(\"OccurrenceStatus\" = 'TerminalMissingMessage' AND \"OccurrenceDueAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc ",
                                "AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL ",
                                "AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NOT NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR ",
                                "AND \"OccurrenceAttemptCount\" = 0 AND \"OccurrenceMessage\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(OccurrenceStatus = 'TerminalInvalidTimeZone' AND OccurrenceDueAtUtc IS NULL ",
                                "(\"OccurrenceStatus\" = 'TerminalInvalidTimeZone' AND \"OccurrenceDueAtUtc\" IS NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceExpiresAtUtc IS NULL AND OccurrenceNextAttemptAtUtc IS NULL ",
                                "AND \"OccurrenceExpiresAtUtc\" IS NULL AND \"OccurrenceNextAttemptAtUtc\" IS NULL "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount = 0 ",
                                "AND \"OccurrenceCompletedAtUtc\" IS NOT NULL AND \"OccurrenceAttemptCount\" = 0 "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND OccurrenceMessage IS NULL)",
                                "AND \"OccurrenceMessage\" IS NULL)"
                            )
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

        ConfigureAnnouncementSchedules(modelBuilder);
    }
}
