using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AutomationTraceOutcomeSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ObservedFailures",
                table: "automation_traces",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TerminalOutcome",
                table: "automation_traces",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ObservedFailures",
                table: "automation_traces");

            migrationBuilder.DropColumn(
                name: "TerminalOutcome",
                table: "automation_traces");
        }
    }
}
