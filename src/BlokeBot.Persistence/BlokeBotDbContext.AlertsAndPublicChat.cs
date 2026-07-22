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
        modelBuilder.Entity<DurableAlert>(b =>
        {
            b.ToTable(
                "durable_alerts",
                t =>
                    t.HasCheckConstraint(
                        "CK_durable_alerts_Severity",
                        KindIn("Severity", _durableAlertSeverities)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Severity)
                .HasConversion(
                    severity => PersistedEnumTokens<DurableAlertSeverity>.Format(severity),
                    value => PersistedEnumTokens<DurableAlertSeverity>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.Source).HasMaxLength(64);
            b.Property(x => x.SourceKey).HasMaxLength(256);
            b.Property(x => x.Title).HasMaxLength(160);
            b.Property(x => x.Message).HasMaxLength(1000);
            b.Property(x => x.LinkPath).HasMaxLength(256);
            b.Property(x => x.AcknowledgedByLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.AcknowledgedAtUtc });
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.Source,
                    x.SourceKey,
                })
                .IsUnique()
                .HasFilter("\"AcknowledgedAtUtc\" IS NULL");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PublicChatOutboxMessage>(b =>
        {
            b.ToTable(
                "public_chat_outbox",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_Status",
                        KindIn("Status", _publicChatOutboxStatuses)
                    );
                    t.HasCheckConstraint("CK_public_chat_outbox_AttemptCount", "AttemptCount >= 0");
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_SafePreSendFailureCount",
                        "SafePreSendFailureCount >= 0"
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_Channel",
                        "length(trim(Channel)) > 0"
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_DeduplicationKey",
                        "DeduplicationKey IS NULL OR length(DeduplicationKey) = 64"
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_FailurePhase",
                        KindInOrNull("FailurePhase", _publicChatOutboxFailurePhases)
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_State",
                        "(Status = 'Pending' AND length(Message) > 0 "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL "
                            + "AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL "
                            + "AND AttemptCount = 0 AND SafePreSendFailureCount = 0 "
                            + "AND length(DeduplicationKey) = 64 "
                            + "AND NextAttemptAtUtc IS NOT NULL "
                            + "AND FailurePhase IS NULL AND FailureType IS NULL "
                            + "AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR "
                            + "(Status = 'Claimed' AND length(Message) > 0 "
                            + "AND ClaimToken IS NOT NULL AND ClaimSlot = 1 "
                            + "AND ClaimExpiresAtUtc IS NOT NULL "
                            + "AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL "
                            + "AND AttemptCount = 0 AND length(DeduplicationKey) = 64 "
                            + "AND NextAttemptAtUtc IS NOT NULL "
                            + "AND ((SafePreSendFailureCount = 0 "
                            + "AND FailurePhase IS NULL AND FailureType IS NULL "
                            + "AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR "
                            + "(SafePreSendFailureCount > 0 "
                            + "AND FailurePhase = 'Preparation' "
                            + "AND length(FailureType) > 0 AND RejectionCode IS NULL))) OR "
                            + "(Status = 'Sending' AND length(Message) > 0 "
                            + "AND ClaimToken IS NOT NULL AND ClaimSlot = 1 "
                            + "AND ClaimExpiresAtUtc IS NOT NULL "
                            + "AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NULL "
                            + "AND AttemptCount > 0 AND length(DeduplicationKey) = 64 "
                            + "AND NextAttemptAtUtc IS NOT NULL "
                            + "AND FailurePhase IS NULL AND FailureType IS NULL "
                            + "AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR "
                            + "(Status = 'SafePreSendTransient' AND length(Message) > 0 "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL "
                            + "AND CompletedAtUtc IS NULL AND AttemptCount = 0 "
                            + "AND length(DeduplicationKey) = 64 "
                            + "AND NextAttemptAtUtc IS NOT NULL "
                            + "AND SafePreSendFailureCount > 0 "
                            + "AND FailurePhase = 'Preparation' "
                            + "AND length(FailureType) > 0 AND RejectionCode IS NULL) OR "
                            + "(Status = 'SafePreSendExhausted' AND Message IS NULL "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL "
                            + "AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 "
                            + "AND SafePreSendFailureCount > 0 "
                            + "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL "
                            + "AND FailurePhase = 'Preparation' "
                            + "AND length(FailureType) > 0 AND RejectionCode IS NULL) OR "
                            + "(Status IN ('MissingChannel', 'MissingBot') AND Message IS NULL "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL "
                            + "AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 "
                            + "AND SafePreSendFailureCount = 0 "
                            + "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL "
                            + "AND FailurePhase = 'Preparation' "
                            + "AND FailureType IS NULL AND HttpStatusCode IS NULL "
                            + "AND RejectionCode IS NULL) OR "
                            + "(Status = 'Rejected' AND Message IS NULL "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NOT NULL "
                            + "AND CompletedAtUtc IS NOT NULL AND FailurePhase = 'Send' "
                            + "AND AttemptCount > 0 "
                            + "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL "
                            + "AND FailureType IS NULL AND HttpStatusCode IS NULL "
                            + "AND (RejectionCode IS NULL OR length(RejectionCode) > 0)) OR "
                            + "(Status = 'Ambiguous' AND Message IS NULL "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NOT NULL "
                            + "AND CompletedAtUtc IS NOT NULL AND FailurePhase = 'Send' "
                            + "AND AttemptCount > 0 "
                            + "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL "
                            + "AND length(FailureType) > 0 AND RejectionCode IS NULL) OR "
                            + "(Status = 'Unexpected' AND Message IS NULL "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL "
                            + "AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 "
                            + "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL "
                            + "AND FailurePhase = 'Preparation' "
                            + "AND length(FailureType) > 0 AND RejectionCode IS NULL) OR "
                            + "(Status = 'Expired' AND Message IS NULL "
                            + "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL "
                            + "AND CompletedAtUtc IS NOT NULL "
                            + "AND FailurePhase IS NULL AND FailureType IS NULL "
                            + "AND HttpStatusCode IS NULL AND RejectionCode IS NULL)"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Channel).HasMaxLength(128);
            b.Property(x => x.DeduplicationKey).HasMaxLength(64);
            b.Property(x => x.FailurePhase)
                .HasConversion(
                    phase =>
                        phase.HasValue
                            ? PersistedEnumTokens<PublicChatOutboxFailurePhase>.Format(phase.Value)
                            : null,
                    value =>
                        value == null
                            ? null
                            : PersistedEnumTokens<PublicChatOutboxFailurePhase>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.FailureType).HasMaxLength(512);
            b.Property(x => x.RejectionCode).HasMaxLength(128);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<PublicChatOutboxStatus>.Format(status),
                    value => PersistedEnumTokens<PublicChatOutboxStatus>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new
            {
                x.Status,
                x.NextAttemptAtUtc,
                x.CreatedAtUtc,
                x.Id,
            });
            b.HasIndex(x => new { x.Status, x.ClaimExpiresAtUtc });
            b.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
            b.HasIndex(x => x.ClaimToken).IsUnique().HasFilter("\"ClaimToken\" IS NOT NULL");
            b.HasIndex(x => x.ClaimSlot).IsUnique().HasFilter("\"ClaimSlot\" IS NOT NULL");
        });

        modelBuilder.Entity<PublicChatSendReceipt>(b =>
        {
            b.ToTable(
                "public_chat_send_receipts",
                t =>
                    t.HasCheckConstraint(
                        "CK_public_chat_send_receipts_Delivery",
                        "(DeliveredDeduplicationKey IS NULL AND DeliveredAtUtc IS NULL) OR "
                            + "(length(DeliveredDeduplicationKey) = 64 "
                            + "AND DeliveredAtUtc IS NOT NULL)"
                    )
            );
            b.HasKey(x => x.OutboxMessageId);
            b.Property(x => x.OutboxMessageId).ValueGeneratedNever();
            b.Property(x => x.DeliveredDeduplicationKey).HasMaxLength(64);
            b.Property(x => x.TwitchMessageId).HasMaxLength(128);
            b.HasIndex(x => x.AttemptedAtUtc);
            b.HasIndex(x => x.DeliveredAtUtc);
        });

        modelBuilder.Entity<ReplyPinPolicy>(b =>
        {
            b.ToTable(
                "reply_pin_policies",
                t =>
                    t.HasCheckConstraint(
                        "CK_reply_pin_policies_DurationSeconds",
                        "DurationSeconds IS NULL OR DurationSeconds BETWEEN 30 AND 1800"
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Feature).HasMaxLength(64);
            b.Property(x => x.ReplyKey).HasMaxLength(128);
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.Feature,
                    x.ReplyKey,
                })
                .IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PublicChatPinOperation>(b =>
        {
            b.ToTable(
                "public_chat_pin_operations",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_public_chat_pin_operations_Kind",
                        KindIn("Kind", _publicChatPinOperationKinds)
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_pin_operations_Status",
                        KindIn("Status", _publicChatPinOperationStatuses)
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_pin_operations_DurationSeconds",
                        "DurationSeconds IS NULL OR DurationSeconds BETWEEN 30 AND 1800"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<PublicChatPinOperationKind>.Format(value),
                    value => PersistedEnumTokens<PublicChatPinOperationKind>.Parse(value)
                )
                .HasMaxLength(16);
            b.Property(x => x.Status)
                .HasConversion(
                    value => PersistedEnumTokens<PublicChatPinOperationStatus>.Format(value),
                    value => PersistedEnumTokens<PublicChatPinOperationStatus>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.Channel).HasMaxLength(128);
            b.Property(x => x.Feature).HasMaxLength(64);
            b.Property(x => x.ReplyKey).HasMaxLength(128);
            b.Property(x => x.TwitchMessageId).HasMaxLength(128);
            b.Property(x => x.PinnerTwitchUserId).HasMaxLength(128);
            b.Property(x => x.Outcome).HasMaxLength(512);
            b.HasIndex(x => new
            {
                x.Status,
                x.CreatedAtUtc,
                x.Id,
            });
            b.HasIndex(x => x.OutboxMessageId)
                .IsUnique()
                .HasFilter("\"OutboxMessageId\" IS NOT NULL");
            b.HasOne<PublicChatOutboxMessage>()
                .WithOne()
                .HasForeignKey<PublicChatPinOperation>(x => x.OutboxMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActivePublicChatPin>(b =>
        {
            b.ToTable("active_public_chat_pins");
            b.HasKey(x => x.Id);
            b.Property(x => x.Channel).HasMaxLength(128);
            b.Property(x => x.TwitchMessageId).HasMaxLength(128);
            b.Property(x => x.PinnerTwitchUserId).HasMaxLength(128);
            b.Property(x => x.Feature).HasMaxLength(64);
            b.Property(x => x.ReplyKey).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.Channel }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
