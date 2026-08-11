using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_CommunityProgressOverlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances",
                sql: "Type IN ('community-goal', 'cue-player', 'empty', 'event-feed', 'giveaway', 'guessing', 'viewer-funded-bounty', 'viewer-queue')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances",
                sql: "Type IN ('cue-player', 'empty', 'event-feed', 'giveaway', 'guessing', 'viewer-queue')"
            );
        }
    }
}
