using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginPackageProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_ActiveRuntime",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ActivePackageOperationId",
                table: "plugin_lifecycles",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SelectedPackageOperationId",
                table: "plugin_lifecycles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.Sql(
                "UPDATE \"plugin_lifecycles\" SET \"SelectedPackageOperationId\" = \"OperationId\", "
                    + "\"ActivePackageOperationId\" = \"ActiveOperationId\";"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_ActiveRuntime",
                table: "plugin_lifecycles",
                sql: "(\"ActiveVersion\" IS NULL AND \"ActiveTag\" IS NULL AND \"ActiveOperationId\" IS NULL AND \"ActivePackageOperationId\" IS NULL AND \"ActiveGeneration\" IS NULL) OR (\"ActiveVersion\" IS NOT NULL AND \"ActiveTag\" IS NOT NULL AND \"ActiveOperationId\" IS NOT NULL AND \"ActivePackageOperationId\" IS NOT NULL AND \"ActiveGeneration\" > 0)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles",
                sql: "\"Phase\" <> 'Faulted' OR \"ActiveOperationId\" IS NULL OR (\"FaultedFrom\" = 'Active' AND \"ActiveVersion\" = \"SelectedVersion\" AND \"ActiveTag\" = \"SelectedTag\" AND \"ActiveOperationId\" = \"OperationId\" AND \"ActivePackageOperationId\" = \"SelectedPackageOperationId\" AND \"ActiveGeneration\" = \"SelectedGeneration\")"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_SelectedPackageOperation",
                table: "plugin_lifecycles",
                sql: "\"SelectedPackageOperationId\" <> '00000000-0000-0000-0000-000000000000'"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_ActiveRuntime",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_SelectedPackageOperation",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropColumn(
                name: "ActivePackageOperationId",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropColumn(
                name: "SelectedPackageOperationId",
                table: "plugin_lifecycles"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_ActiveRuntime",
                table: "plugin_lifecycles",
                sql: "(\"ActiveVersion\" IS NULL AND \"ActiveTag\" IS NULL AND \"ActiveOperationId\" IS NULL AND \"ActiveGeneration\" IS NULL) OR (\"ActiveVersion\" IS NOT NULL AND \"ActiveTag\" IS NOT NULL AND \"ActiveOperationId\" IS NOT NULL AND \"ActiveGeneration\" > 0)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles",
                sql: "\"Phase\" <> 'Faulted' OR \"ActiveOperationId\" IS NULL OR (\"FaultedFrom\" = 'Active' AND \"ActiveVersion\" = \"SelectedVersion\" AND \"ActiveTag\" = \"SelectedTag\" AND \"ActiveOperationId\" = \"OperationId\" AND \"ActiveGeneration\" = \"SelectedGeneration\")"
            );
        }
    }
}
