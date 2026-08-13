using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_RaidShoutoutNotEligible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history",
                sql: "ShoutoutOutcome IN ('Cooldown', 'Deduplicated', 'NotConfigured', 'NotEligible', 'Rejected', 'Sent', 'Suppressed')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history",
                sql: "ShoutoutOutcome IN ('Cooldown', 'Deduplicated', 'NotConfigured', 'Rejected', 'Sent', 'Suppressed', 'TargetOffline')"
            );
        }
    }
}
