using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static string PublicChatOutboxTerminalConstraint(ModelBuilder modelBuilder) =>
        ProviderSql(
            modelBuilder,
            "(Status IN ('MissingChannel', 'MissingBot') AND Message IS NULL ",
            "(\"Status\" IN ('MissingChannel', 'MissingBot') AND \"Message\" IS NULL "
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
            "AND SafePreSendFailureCount = 0 ",
            "AND \"SafePreSendFailureCount\" = 0 "
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
            "AND FailureType IS NULL AND HttpStatusCode IS NULL ",
            "AND \"FailureType\" IS NULL AND \"HttpStatusCode\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND RejectionCode IS NULL) OR ",
            "AND \"RejectionCode\" IS NULL) OR "
        )
        + ProviderSql(
            modelBuilder,
            "(Status = 'Rejected' AND Message IS NULL ",
            "(\"Status\" = 'Rejected' AND \"Message\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimToken IS NULL AND ClaimSlot IS NULL ",
            "AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NOT NULL ",
            "AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND CompletedAtUtc IS NOT NULL AND FailurePhase = 'Send' ",
            "AND \"CompletedAtUtc\" IS NOT NULL AND \"FailurePhase\" = 'Send' "
        )
        + ProviderSql(modelBuilder, "AND AttemptCount > 0 ", "AND \"AttemptCount\" > 0 ")
        + ProviderSql(
            modelBuilder,
            "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL ",
            "AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND FailureType IS NULL AND HttpStatusCode IS NULL ",
            "AND \"FailureType\" IS NULL AND \"HttpStatusCode\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND (RejectionCode IS NULL OR length(RejectionCode) > 0)) OR ",
            "AND (\"RejectionCode\" IS NULL OR length(\"RejectionCode\") > 0)) OR "
        )
        + ProviderSql(
            modelBuilder,
            "(Status = 'Ambiguous' AND Message IS NULL ",
            "(\"Status\" = 'Ambiguous' AND \"Message\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimToken IS NULL AND ClaimSlot IS NULL ",
            "AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NOT NULL ",
            "AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND CompletedAtUtc IS NOT NULL AND FailurePhase = 'Send' ",
            "AND \"CompletedAtUtc\" IS NOT NULL AND \"FailurePhase\" = 'Send' "
        )
        + ProviderSql(modelBuilder, "AND AttemptCount > 0 ", "AND \"AttemptCount\" > 0 ")
        + ProviderSql(
            modelBuilder,
            "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL ",
            "AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND length(FailureType) > 0 AND RejectionCode IS NULL) OR ",
            "AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL) OR "
        )
        + ProviderSql(
            modelBuilder,
            "(Status = 'Unexpected' AND Message IS NULL ",
            "(\"Status\" = 'Unexpected' AND \"Message\" IS NULL "
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
        )
        + ProviderSql(
            modelBuilder,
            "(Status = 'Expired' AND Message IS NULL ",
            "(\"Status\" = 'Expired' AND \"Message\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL ",
            "AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL "
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
            "AND CompletedAtUtc IS NOT NULL ",
            "AND \"CompletedAtUtc\" IS NOT NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND FailurePhase IS NULL AND FailureType IS NULL ",
            "AND \"FailurePhase\" IS NULL AND \"FailureType\" IS NULL "
        )
        + ProviderSql(
            modelBuilder,
            "AND HttpStatusCode IS NULL AND RejectionCode IS NULL)",
            "AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL)"
        );
}
