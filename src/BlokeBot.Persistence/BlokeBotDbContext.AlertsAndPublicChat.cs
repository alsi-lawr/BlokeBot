using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _durableAlertSeverities =
        PersistedEnumTokens<DurableAlertSeverity>.Values.ToArray();

    private static readonly string[] _publicChatOutboxStatuses =
        PersistedEnumTokens<PublicChatOutboxStatus>.Values.ToArray();

    private static readonly string[] _publicChatOutboxFailurePhases =
        PersistedEnumTokens<PublicChatOutboxFailurePhase>.Values.ToArray();

    private static readonly string[] _publicChatPinOperationKinds =
        PersistedEnumTokens<PublicChatPinOperationKind>.Values.ToArray();

    private static readonly string[] _publicChatPinOperationStatuses =
        PersistedEnumTokens<PublicChatPinOperationStatus>.Values.ToArray();

    private static void ConfigureAlertsAndPublicChat(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<DurableAlert>(b =>
        {
            _ = b.ToTable(
                "durable_alerts",
                t =>
                    t.HasCheckConstraint(
                        "CK_durable_alerts_Severity",
                        KindIn(modelBuilder, "Severity", _durableAlertSeverities)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Severity)
                .HasConversion(
                    static severity => PersistedEnumTokens<DurableAlertSeverity>.Format(severity),
                    static value => PersistedEnumTokens<DurableAlertSeverity>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.Source).HasMaxLength(64);
            _ = b.Property(static x => x.SourceKey).HasMaxLength(256);
            _ = b.Property(static x => x.Title).HasMaxLength(160);
            _ = b.Property(static x => x.Message).HasMaxLength(1000);
            _ = b.Property(static x => x.LinkPath).HasMaxLength(256);
            _ = b.Property(static x => x.AcknowledgedByLogin).HasMaxLength(128);
            _ = b.Property(static x => x.OccurrenceCount).HasDefaultValue(1);
            _ = b.HasIndex(static x => new { x.HostId, x.AcknowledgedAtUtc });
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.Source,
                    x.SourceKey,
                })
                .IsUnique()
                .HasFilter("\"AcknowledgedAtUtc\" IS NULL");
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PublicChatOutboxMessage>(b =>
        {
            _ = b.ToTable(
                "public_chat_outbox",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_outbox_Status",
                        KindIn(modelBuilder, "Status", _publicChatOutboxStatuses)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_outbox_AttemptCount",
                        ProviderSql(modelBuilder, "AttemptCount >= 0", "\"AttemptCount\" >= 0")
                    );
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_outbox_SafePreSendFailureCount",
                        ProviderSql(
                            modelBuilder,
                            "SafePreSendFailureCount >= 0",
                            "\"SafePreSendFailureCount\" >= 0"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_outbox_Channel",
                        ProviderSql(
                            modelBuilder,
                            "length(trim(Channel)) > 0",
                            "length(trim(\"Channel\")) > 0"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_outbox_DeduplicationKey",
                        ProviderSql(
                            modelBuilder,
                            "DeduplicationKey IS NULL OR length(DeduplicationKey) = 64",
                            "\"DeduplicationKey\" IS NULL OR length(\"DeduplicationKey\") = 64"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_outbox_FailurePhase",
                        KindInOrNull(modelBuilder, "FailurePhase", _publicChatOutboxFailurePhases)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_outbox_State",
                        PublicChatOutboxStateConstraint(modelBuilder)
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Channel).HasMaxLength(128);
            _ = b.Property(static x => x.DeduplicationKey).HasMaxLength(64);
            _ = b.Property(static x => x.FailurePhase)
                .HasConversion(
                    static phase =>
                        phase.HasValue
                            ? PersistedEnumTokens<PublicChatOutboxFailurePhase>.Format(phase.Value)
                            : null,
                    static value =>
                        value == null
                            ? null
                            : PersistedEnumTokens<PublicChatOutboxFailurePhase>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.FailureType).HasMaxLength(512);
            _ = b.Property(static x => x.RejectionCode).HasMaxLength(128);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static status => PersistedEnumTokens<PublicChatOutboxStatus>.Format(status),
                    static value => PersistedEnumTokens<PublicChatOutboxStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new
            {
                x.Status,
                x.NextAttemptAtUtc,
                x.CreatedAtUtc,
                x.Id,
            });
            _ = b.HasIndex(static x => new { x.Status, x.ClaimExpiresAtUtc });
            _ = b.HasIndex(static x => new { x.Status, x.ExpiresAtUtc });
            _ = b.HasIndex(static x => x.ClaimToken)
                .IsUnique()
                .HasFilter("\"ClaimToken\" IS NOT NULL");
            _ = b.HasIndex(static x => x.ClaimSlot)
                .IsUnique()
                .HasFilter("\"ClaimSlot\" IS NOT NULL");
        });

        _ = modelBuilder.Entity<PublicChatSendReceipt>(b =>
        {
            _ = b.ToTable(
                "public_chat_send_receipts",
                t =>
                    t.HasCheckConstraint(
                        "CK_public_chat_send_receipts_Delivery",
                        ProviderSql(
                            modelBuilder,
                            "(DeliveredDeduplicationKey IS NULL AND DeliveredAtUtc IS NULL) OR ",
                            "(\"DeliveredDeduplicationKey\" IS NULL AND \"DeliveredAtUtc\" IS NULL) OR "
                        )
                            + ProviderSql(
                                modelBuilder,
                                "(length(DeliveredDeduplicationKey) = 64 ",
                                "(length(\"DeliveredDeduplicationKey\") = 64 "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "AND DeliveredAtUtc IS NOT NULL)",
                                "AND \"DeliveredAtUtc\" IS NOT NULL)"
                            )
                    )
            );
            _ = b.HasKey(static x => x.OutboxMessageId);
            _ = b.Property(static x => x.OutboxMessageId).ValueGeneratedNever();
            _ = b.Property(static x => x.DeliveredDeduplicationKey).HasMaxLength(64);
            _ = b.Property(static x => x.TwitchMessageId).HasMaxLength(128);
            _ = b.HasIndex(static x => x.AttemptedAtUtc);
            _ = b.HasIndex(static x => x.DeliveredAtUtc);
        });

        _ = modelBuilder.Entity<ReplyPinPolicy>(b =>
        {
            _ = b.ToTable(
                "reply_pin_policies",
                t =>
                    t.HasCheckConstraint(
                        "CK_reply_pin_policies_DurationSeconds",
                        ProviderSql(
                            modelBuilder,
                            "DurationSeconds IS NULL OR DurationSeconds BETWEEN 30 AND 1800",
                            "\"DurationSeconds\" IS NULL OR \"DurationSeconds\" BETWEEN 30 AND 1800"
                        )
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Feature).HasMaxLength(64);
            _ = b.Property(static x => x.ReplyKey).HasMaxLength(128);
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.Feature,
                    x.ReplyKey,
                })
                .IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PublicChatPinOperation>(b =>
        {
            _ = b.ToTable(
                "public_chat_pin_operations",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_pin_operations_Kind",
                        KindIn(modelBuilder, "Kind", _publicChatPinOperationKinds)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_pin_operations_Status",
                        KindIn(modelBuilder, "Status", _publicChatPinOperationStatuses)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_public_chat_pin_operations_DurationSeconds",
                        ProviderSql(
                            modelBuilder,
                            "DurationSeconds IS NULL OR DurationSeconds BETWEEN 30 AND 1800",
                            "\"DurationSeconds\" IS NULL OR \"DurationSeconds\" BETWEEN 30 AND 1800"
                        )
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<PublicChatPinOperationKind>.Format(value),
                    static value => PersistedEnumTokens<PublicChatPinOperationKind>.Parse(value)
                )
                .HasMaxLength(16);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static value => PersistedEnumTokens<PublicChatPinOperationStatus>.Format(value),
                    static value => PersistedEnumTokens<PublicChatPinOperationStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.Channel).HasMaxLength(128);
            _ = b.Property(static x => x.Feature).HasMaxLength(64);
            _ = b.Property(static x => x.ReplyKey).HasMaxLength(128);
            _ = b.Property(static x => x.TwitchMessageId).HasMaxLength(128);
            _ = b.Property(static x => x.PinnerTwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.Outcome).HasMaxLength(512);
            _ = b.HasIndex(static x => new
            {
                x.Status,
                x.CreatedAtUtc,
                x.Id,
            });
            _ = b.HasIndex(static x => x.OutboxMessageId)
                .IsUnique()
                .HasFilter("\"OutboxMessageId\" IS NOT NULL");
            _ = b.HasOne<PublicChatOutboxMessage>()
                .WithOne()
                .HasForeignKey<PublicChatPinOperation>(static x => x.OutboxMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<ActivePublicChatPin>(b =>
        {
            _ = b.ToTable("active_public_chat_pins");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Channel).HasMaxLength(128);
            _ = b.Property(static x => x.TwitchMessageId).HasMaxLength(128);
            _ = b.Property(static x => x.PinnerTwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.Feature).HasMaxLength(64);
            _ = b.Property(static x => x.ReplyKey).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.HostId, x.Channel }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
