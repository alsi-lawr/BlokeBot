using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginLifecycleTombstoneInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_Phase",
                table: "plugin_lifecycles"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_Phase",
                table: "plugin_lifecycles",
                sql: "\"Phase\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Faulted')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_Phase",
                table: "plugin_lifecycles"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_Phase",
                table: "plugin_lifecycles",
                sql: "\"Phase\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Purged', 'Faulted')"
            );
        }
    }
}
