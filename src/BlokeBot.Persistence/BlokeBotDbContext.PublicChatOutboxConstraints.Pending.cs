using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static string PublicChatOutboxStateConstraint(ModelBuilder modelBuilder) =>
        PublicChatOutboxPendingConstraint(modelBuilder)
        + PublicChatOutboxTerminalConstraint(modelBuilder);

    private static string PublicChatOutboxPendingConstraint(ModelBuilder modelBuilder) =>
        ProviderSql(
            modelBuilder,
            "(Status = 'Pending' AND length(Message) > 0 ",
            "(\"Status\" = 'Pending' AND length(\"Message\") > 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimToken IS NULL AND ClaimSlot IS NULL ",
            "AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimExpiresAtUtc IS NULL ",
            "AND \"ClaimExpiresAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL ",
            "AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND AttemptCount = 0 AND SafePreSendFailureCount = 0 ",
            "AND \"AttemptCount\" = 0 AND \"SafePreSendFailureCount\" = 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND length(DeduplicationKey) = 64 ",
            "AND length(\"DeduplicationKey\") = 64 "
        )
        + ProviderSql(
            modelBuilder,
            "AND NextAttemptAtUtc IS NOT NULL ",
            "AND \"NextAttemptAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND FailurePhase IS NULL AND FailureType IS NULL ",
            "AND \"FailurePhase\" IS NULL AND \"FailureType\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR ",
            "AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL) OR "
        )
        + ProviderSql(
            modelBuilder,
            "(Status = 'Claimed' AND length(Message) > 0 ",
            "(\"Status\" = 'Claimed' AND length(\"Message\") > 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimToken IS NOT NULL AND ClaimSlot = 1 ",
            "AND \"ClaimToken\" IS NOT NULL AND \"ClaimSlot\" = 1 "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimExpiresAtUtc IS NOT NULL ",
            "AND \"ClaimExpiresAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL ",
            "AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND AttemptCount = 0 AND length(DeduplicationKey) = 64 ",
            "AND \"AttemptCount\" = 0 AND length(\"DeduplicationKey\") = 64 "
        )
        + ProviderSql(
            modelBuilder,
            "AND NextAttemptAtUtc IS NOT NULL ",
            "AND \"NextAttemptAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ((SafePreSendFailureCount = 0 ",
            "AND ((\"SafePreSendFailureCount\" = 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND FailurePhase IS NULL AND FailureType IS NULL ",
            "AND \"FailurePhase\" IS NULL AND \"FailureType\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR ",
            "AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL) OR "
        )
        + ProviderSql(
            modelBuilder,
            "(SafePreSendFailureCount > 0 ",
            "(\"SafePreSendFailureCount\" > 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND FailurePhase = 'Preparation' ",
            "AND \"FailurePhase\" = 'Preparation' "
        )
        + ProviderSql(
            modelBuilder,
            "AND length(FailureType) > 0 AND RejectionCode IS NULL))) OR ",
            "AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL))) OR "
        )
        + ProviderSql(
            modelBuilder,
            "(Status = 'Sending' AND length(Message) > 0 ",
            "(\"Status\" = 'Sending' AND length(\"Message\") > 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimToken IS NOT NULL AND ClaimSlot = 1 ",
            "AND \"ClaimToken\" IS NOT NULL AND \"ClaimSlot\" = 1 "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimExpiresAtUtc IS NOT NULL ",
            "AND \"ClaimExpiresAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NULL ",
            "AND \"SendStartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND AttemptCount > 0 AND length(DeduplicationKey) = 64 ",
            "AND \"AttemptCount\" > 0 AND length(\"DeduplicationKey\") = 64 "
        )
        + ProviderSql(
            modelBuilder,
            "AND NextAttemptAtUtc IS NOT NULL ",
            "AND \"NextAttemptAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND FailurePhase IS NULL AND FailureType IS NULL ",
            "AND \"FailurePhase\" IS NULL AND \"FailureType\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR ",
            "AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL) OR "
        )
        + ProviderSql(
            modelBuilder,
            "(Status = 'SafePreSendTransient' AND length(Message) > 0 ",
            "(\"Status\" = 'SafePreSendTransient' AND length(\"Message\") > 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimToken IS NULL AND ClaimSlot IS NULL ",
            "AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL ",
            "AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND CompletedAtUtc IS NULL AND AttemptCount = 0 ",
            "AND \"CompletedAtUtc\" IS NULL AND \"AttemptCount\" = 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND length(DeduplicationKey) = 64 ",
            "AND length(\"DeduplicationKey\") = 64 "
        )
        + ProviderSql(
            modelBuilder,
            "AND NextAttemptAtUtc IS NOT NULL ",
            "AND \"NextAttemptAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND SafePreSendFailureCount > 0 ",
            "AND \"SafePreSendFailureCount\" > 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND FailurePhase = 'Preparation' ",
            "AND \"FailurePhase\" = 'Preparation' "
        )
        + ProviderSql(
            modelBuilder,
            "AND length(FailureType) > 0 AND RejectionCode IS NULL) OR ",
            "AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL) OR "
        )
        + ProviderSql(
            modelBuilder,
            "(Status = 'SafePreSendExhausted' AND Message IS NULL ",
            "(\"Status\" = 'SafePreSendExhausted' AND \"Message\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimToken IS NULL AND ClaimSlot IS NULL ",
            "AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL ",
            "AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 ",
            "AND \"CompletedAtUtc\" IS NOT NULL AND \"AttemptCount\" = 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND SafePreSendFailureCount > 0 ",
            "AND \"SafePreSendFailureCount\" > 0 "
        )
        + ProviderSql(
            modelBuilder,
            "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL ",
            "AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND FailurePhase = 'Preparation' ",
            "AND \"FailurePhase\" = 'Preparation' "
        )
        + ProviderSql(
            modelBuilder,
            "AND length(FailureType) > 0 AND RejectionCode IS NULL) OR ",
            "AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL) OR "
        );
}
