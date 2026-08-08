#pragma warning disable

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v070_CustomCommandAutomationRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_command_actions_ActionType",
                table: "custom_command_actions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_command_actions_Payload",
                table: "custom_command_actions"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_command_actions_ActionType",
                table: "custom_command_actions",
                sql: "ActionType IN ('Automation', 'Counter', 'Message', 'OverlayCue')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_command_actions_Payload",
                table: "custom_command_actions",
                sql: "(ActionType IN ('Message', 'Automation') AND CounterId IS NULL AND TargetOverlayPublicId IS NULL AND CuePublicId IS NULL AND QueuePolicy IS NULL AND ReplyOrder IS NULL) OR (ActionType = 'Counter' AND CounterId IS NOT NULL AND TargetOverlayPublicId IS NULL AND CuePublicId IS NULL AND QueuePolicy IS NULL AND ReplyOrder IS NULL) OR (ActionType = 'OverlayCue' AND CounterId IS NULL AND TargetOverlayPublicId IS NOT NULL AND CuePublicId IS NOT NULL AND QueuePolicy IS NOT NULL AND ReplyOrder IS NOT NULL)"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_command_actions_ActionType",
                table: "custom_command_actions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_command_actions_Payload",
                table: "custom_command_actions"
            );

            migrationBuilder.Sql(
                "UPDATE custom_command_actions SET ActionType = 'Message' WHERE ActionType = 'Automation';"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_command_actions_ActionType",
                table: "custom_command_actions",
                sql: "ActionType IN ('Counter', 'Message', 'OverlayCue')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_command_actions_Payload",
                table: "custom_command_actions",
                sql: "(ActionType = 'Message' AND CounterId IS NULL AND TargetOverlayPublicId IS NULL AND CuePublicId IS NULL AND QueuePolicy IS NULL AND ReplyOrder IS NULL) OR (ActionType = 'Counter' AND CounterId IS NOT NULL AND TargetOverlayPublicId IS NULL AND CuePublicId IS NULL AND QueuePolicy IS NULL AND ReplyOrder IS NULL) OR (ActionType = 'OverlayCue' AND CounterId IS NULL AND TargetOverlayPublicId IS NOT NULL AND CuePublicId IS NOT NULL AND QueuePolicy IS NOT NULL AND ReplyOrder IS NOT NULL)"
            );
        }
    }
}
