using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ViewerOwnedCountdowns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_automation_countdowns",
                table: "automation_countdowns"
            );

            migrationBuilder.AddColumn<string>(
                name: "OwnerViewerId",
                table: "automation_countdowns",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "OwnerDisplayName",
                table: "automation_countdowns",
                type: "TEXT",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddPrimaryKey(
                name: "PK_automation_countdowns",
                table: "automation_countdowns",
                columns: new[] { "HostId", "OwnerViewerId", "Name" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_automation_countdowns",
                table: "automation_countdowns"
            );

            migrationBuilder.DropColumn(name: "OwnerViewerId", table: "automation_countdowns");

            migrationBuilder.DropColumn(name: "OwnerDisplayName", table: "automation_countdowns");

            migrationBuilder.AddPrimaryKey(
                name: "PK_automation_countdowns",
                table: "automation_countdowns",
                columns: new[] { "HostId", "Name" }
            );
        }
    }
}
