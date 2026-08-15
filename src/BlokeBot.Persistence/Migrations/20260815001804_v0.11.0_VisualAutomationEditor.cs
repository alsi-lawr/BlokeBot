using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0110_VisualAutomationEditor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CanvasX",
                table: "automation_flow_nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "CanvasY",
                table: "automation_flow_nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CanvasX", table: "automation_flow_nodes");

            migrationBuilder.DropColumn(name: "CanvasY", table: "automation_flow_nodes");
        }
    }
}
