using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_BountyPauseAdjustedAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_bounty_moderation_audit_Action",
                table: "bounty_moderation_audit"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_bounty_moderation_audit_Action",
                table: "bounty_moderation_audit",
                sql: "Action IN ('Accepted', 'Cancelled', 'Completed', 'Created', 'Expired', 'Extended', 'Failed', 'FundingOpened', 'PauseAdjusted', 'Rejected')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_bounty_moderation_audit_Action",
                table: "bounty_moderation_audit"
            );

            migrationBuilder.Sql(
                "UPDATE bounty_moderation_audit SET Action = 'Extended' WHERE Action = 'PauseAdjusted';"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_bounty_moderation_audit_Action",
                table: "bounty_moderation_audit",
                sql: "Action IN ('Accepted', 'Cancelled', 'Completed', 'Created', 'Expired', 'Extended', 'Failed', 'FundingOpened', 'Rejected')"
            );
        }
    }
}
