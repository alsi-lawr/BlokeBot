using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginLifecycleReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_OperationKind",
                table: "plugin_lifecycles"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_OperationKind",
                table: "plugin_lifecycles",
                sql: "\"OperationKind\" IN ('Activate', 'Remove', 'Replace', 'Restart')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_OperationKind",
                table: "plugin_lifecycles"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_OperationKind",
                table: "plugin_lifecycles",
                sql: "\"OperationKind\" IN ('Activate', 'Remove', 'Restart')"
            );
        }
    }
}
