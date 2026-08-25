using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_ConfigurationActivationOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations"
            );

            migrationBuilder.DropColumn(name: "FailureCode", table: "configuration_activations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations",
                sql: "Status IN ('Complete', 'Failed', 'ManualFollowUp', 'Pending', 'Processing')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "configuration_activations",
                type: "TEXT",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations",
                sql: "Status IN ('Complete', 'Failed', 'Pending', 'Processing')"
            );
        }
    }
}
