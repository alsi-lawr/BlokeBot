using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginLifecycleFaultInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"plugin_lifecycles\" SET \"FaultedFrom\" = NULL "
                    + "WHERE \"Phase\" <> 'Faulted' AND \"FaultedFrom\" IS NOT NULL;"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles",
                sql: "(\"Phase\" = 'Faulted' AND \"FaultedFrom\" IS NOT NULL AND \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Purging')) OR (\"Phase\" <> 'Faulted' AND \"FaultedFrom\" IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles",
                sql: "\"Phase\" <> 'Faulted' OR \"ActiveOperationId\" IS NULL OR (\"FaultedFrom\" = 'Active' AND \"ActiveVersion\" = \"SelectedVersion\" AND \"ActiveTag\" = \"SelectedTag\" AND \"ActiveOperationId\" = \"OperationId\" AND \"ActiveGeneration\" = \"SelectedGeneration\")"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles",
                sql: "\"FaultedFrom\" IS NULL OR \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Faulted')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles",
                sql: "\"Phase\" <> 'Faulted' OR \"ActiveOperationId\" IS NULL OR \"FaultedFrom\" = 'Active'"
            );
        }
    }
}
