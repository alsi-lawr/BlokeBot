using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginAutomations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnavailableReason",
                table: "automation_flows",
                type: "TEXT",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PluginProvenanceJson",
                table: "automation_flow_nodes",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "plugin_automation_instantiations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnableOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FeatureId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PluginVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ManifestVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Diagnostic = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_automation_instantiations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plugin_automation_instantiations_automation_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "automation_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_plugin_automation_instantiations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_EnableOperationId_TemplateId",
                table: "plugin_automation_instantiations",
                columns: new[] { "EnableOperationId", "TemplateId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_FlowId",
                table: "plugin_automation_instantiations",
                column: "FlowId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_HostId",
                table: "plugin_automation_instantiations",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_PluginId_FeatureId_HostId_TemplateId_TemplateHash",
                table: "plugin_automation_instantiations",
                columns: new[] { "PluginId", "FeatureId", "HostId", "TemplateId", "TemplateHash" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "plugin_automation_instantiations");

            migrationBuilder.DropColumn(name: "UnavailableReason", table: "automation_flows");

            migrationBuilder.DropColumn(
                name: "PluginProvenanceJson",
                table: "automation_flow_nodes"
            );
        }
    }
}
