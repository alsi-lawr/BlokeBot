using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0110_AutomationNodeDisplayAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "automation_flow_edges",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Flow"
            );

            migrationBuilder.AddColumn<string>(
                name: "DisplayAlias",
                table: "automation_flow_nodes",
                type: "TEXT",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "OutputJson",
                table: "automation_node_runs",
                type: "TEXT",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OutputJson", table: "automation_node_runs");

            migrationBuilder.DropColumn(name: "DisplayAlias", table: "automation_flow_nodes");

            migrationBuilder.DropColumn(name: "Kind", table: "automation_flow_edges");
        }
    }
}
