#pragma warning disable

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v060_CustomCommandOverlayCues : Migration
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

            migrationBuilder.AddColumn<string>(
                name: "CuePublicId",
                table: "custom_command_actions",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "QueuePolicy",
                table: "custom_command_actions",
                type: "TEXT",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReplyOrder",
                table: "custom_command_actions",
                type: "TEXT",
                maxLength: 16,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetOverlayPublicId",
                table: "custom_command_actions",
                type: "TEXT",
                nullable: true
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_command_actions_QueuePolicy",
                table: "custom_command_actions",
                sql: "QueuePolicy IS NULL OR QueuePolicy IN ('concurrent', 'enqueue', 'ignore', 'replace')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_command_actions_ReplyOrder",
                table: "custom_command_actions",
                sql: "ReplyOrder IS NULL OR ReplyOrder IN ('after', 'before')"
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

            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_command_actions_QueuePolicy",
                table: "custom_command_actions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_command_actions_ReplyOrder",
                table: "custom_command_actions"
            );

            migrationBuilder.DropColumn(name: "CuePublicId", table: "custom_command_actions");

            migrationBuilder.DropColumn(name: "QueuePolicy", table: "custom_command_actions");

            migrationBuilder.DropColumn(name: "ReplyOrder", table: "custom_command_actions");

            migrationBuilder.DropColumn(
                name: "TargetOverlayPublicId",
                table: "custom_command_actions"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_command_actions_ActionType",
                table: "custom_command_actions",
                sql: "ActionType IN ('Counter', 'Message')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_command_actions_Payload",
                table: "custom_command_actions",
                sql: "(ActionType = 'Message' AND CounterId IS NULL) OR (ActionType = 'Counter' AND CounterId IS NOT NULL)"
            );
        }
    }
}
