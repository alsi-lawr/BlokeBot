using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_AutomaticRaidDeliveryOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_ResultCode",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_State",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_Status",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history",
                sql: "ShoutoutOutcome IN ('Cooldown', 'Deduplicated', 'NotConfigured', 'NotEligible', 'Queued', 'Rejected', 'Sent', 'Suppressed')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_ResultCode",
                table: "automatic_raid_shoutout_outcomes",
                sql: "ResultCode IS NULL OR ResultCode IN ('Ambiguous', 'AuthorityRequired', 'Cooldown', 'Delivered', 'Invalid', 'NotReady', 'PartialFailure', 'Queued', 'RateLimited', 'Rejected', 'RuntimeMessageTooLong', 'Unexpected')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_State",
                table: "automatic_raid_shoutout_outcomes",
                sql: "(Status = 'Processing' AND ResultCode IS NULL AND CompletedAtUtc IS NULL) OR (Status = 'Queued' AND ResultCode = 'Queued' AND CompletedAtUtc IS NULL) OR (Status = 'Delivered' AND ResultCode = 'Delivered' AND CompletedAtUtc IS NOT NULL) OR (Status = 'NotDelivered' AND ResultCode IS NOT NULL AND ResultCode NOT IN ('Queued', 'Delivered', 'Ambiguous') AND CompletedAtUtc IS NOT NULL) OR (Status = 'Ambiguous' AND ResultCode = 'Ambiguous' AND CompletedAtUtc IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_Status",
                table: "automatic_raid_shoutout_outcomes",
                sql: "Status IN ('Ambiguous', 'Delivered', 'NotDelivered', 'Processing', 'Queued')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_ResultCode",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_State",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_Status",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.Sql(
                "UPDATE raid_collaboration_history SET ShoutoutOutcome = 'Rejected' WHERE ShoutoutOutcome = 'Queued';"
            );

            migrationBuilder.Sql(
                "UPDATE automatic_raid_shoutout_outcomes SET Status = 'Processing', ResultCode = NULL, CompletedAtUtc = NULL WHERE Status = 'Queued';"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history",
                sql: "ShoutoutOutcome IN ('Cooldown', 'Deduplicated', 'NotConfigured', 'NotEligible', 'Rejected', 'Sent', 'Suppressed')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_ResultCode",
                table: "automatic_raid_shoutout_outcomes",
                sql: "ResultCode IS NULL OR ResultCode IN ('Ambiguous', 'AuthorityRequired', 'Cooldown', 'Delivered', 'Invalid', 'NotReady', 'PartialFailure', 'RateLimited', 'Rejected', 'RuntimeMessageTooLong', 'Unexpected')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_State",
                table: "automatic_raid_shoutout_outcomes",
                sql: "(Status = 'Processing' AND ResultCode IS NULL AND CompletedAtUtc IS NULL) OR (Status = 'Delivered' AND ResultCode IS NOT NULL AND ResultCode = 'Delivered' AND CompletedAtUtc IS NOT NULL) OR (Status = 'NotDelivered' AND ResultCode IS NOT NULL AND ResultCode NOT IN ('Delivered', 'Ambiguous') AND CompletedAtUtc IS NOT NULL) OR (Status = 'Ambiguous' AND ResultCode IS NOT NULL AND ResultCode = 'Ambiguous' AND CompletedAtUtc IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_Status",
                table: "automatic_raid_shoutout_outcomes",
                sql: "Status IN ('Ambiguous', 'Delivered', 'NotDelivered', 'Processing')"
            );
        }
    }
}
