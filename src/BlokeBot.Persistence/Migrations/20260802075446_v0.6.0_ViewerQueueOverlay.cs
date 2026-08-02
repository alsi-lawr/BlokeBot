using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v060_ViewerQueueOverlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances"
            );

            migrationBuilder.DropColumn(name: "IsRequired", table: "play_queue_fields");

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances",
                sql: "Type IN ('cue-player', 'empty', 'event-feed', 'giveaway', 'guessing', 'viewer-queue')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances"
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsRequired",
                table: "play_queue_fields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances",
                sql: "Type IN ('cue-player', 'empty', 'event-feed', 'giveaway', 'guessing')"
            );
        }
    }
}
