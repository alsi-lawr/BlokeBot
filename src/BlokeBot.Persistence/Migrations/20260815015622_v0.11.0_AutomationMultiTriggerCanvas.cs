using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0110_AutomationMultiTriggerCanvas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_automation_flow_runs_FlowId_SourceDefinitionId_SourceOccurrenceId",
                table: "automation_flow_runs"
            );

            migrationBuilder.AddColumn<bool>(
                name: "UseSmoothEdges",
                table: "automation_flows",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "UseVerticalLayout",
                table: "automation_flows",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SourceNodeId",
                table: "automation_flow_runs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_runs_FlowId_SourceNodeId_SourceOccurrenceId",
                table: "automation_flow_runs",
                columns: new[] { "FlowId", "SourceNodeId", "SourceOccurrenceId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_automation_flow_runs_FlowId_SourceNodeId_SourceOccurrenceId",
                table: "automation_flow_runs"
            );

            migrationBuilder.DropColumn(name: "UseSmoothEdges", table: "automation_flows");

            migrationBuilder.DropColumn(name: "UseVerticalLayout", table: "automation_flows");

            migrationBuilder.DropColumn(name: "SourceNodeId", table: "automation_flow_runs");

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_runs_FlowId_SourceDefinitionId_SourceOccurrenceId",
                table: "automation_flow_runs",
                columns: new[] { "FlowId", "SourceDefinitionId", "SourceOccurrenceId" },
                unique: true
            );
        }
    }
}
