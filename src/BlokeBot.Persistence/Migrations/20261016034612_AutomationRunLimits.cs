using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationRunLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaximumUnfinishedAutomationRuns",
                table: "hosts",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaximumWaitingAutomationRuns",
                table: "hosts",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaximumUnfinishedRuns",
                table: "automation_flows",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaximumWaitingRuns",
                table: "automation_flows",
                type: "INTEGER",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MaximumUnfinishedAutomationRuns", table: "hosts");

            migrationBuilder.DropColumn(name: "MaximumWaitingAutomationRuns", table: "hosts");

            migrationBuilder.DropColumn(name: "MaximumUnfinishedRuns", table: "automation_flows");

            migrationBuilder.DropColumn(name: "MaximumWaitingRuns", table: "automation_flows");
        }
    }
}
