#pragma warning disable

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v060_GuessingOverlay : Migration
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
                sql: "Type IN ('empty', 'guessing')"
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
                sql: "Type IN ('empty')"
            );
        }
    }
}
