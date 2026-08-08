using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v070_AutomationRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutomationGeneration",
                table: "hosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateTable(
                name: "automation_flows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_flows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_flows_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_flow_edges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourcePortId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 96,
                        nullable: false
                    ),
                    TargetNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetPortId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 96,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_flow_edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_flow_edges_automation_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "automation_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_flow_nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefinitionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 96,
                        nullable: false
                    ),
                    DefinitionSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    FieldExpressionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExpressionLanguageVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContinueOnFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_flow_nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_flow_nodes_automation_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "automation_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_flow_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    AutomationGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredFeatures = table.Column<long>(type: "INTEGER", nullable: false),
                    ContextSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceDefinitionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 96,
                        nullable: false
                    ),
                    SourceOccurrenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContextJson = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExecutionLeaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_flow_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_flow_runs_automation_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "automation_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_node_runs",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OutcomeCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_node_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_node_runs_automation_flow_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "automation_flow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_edges_FlowId_TargetNodeId",
                table: "automation_flow_edges",
                columns: new[] { "FlowId", "TargetNodeId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_nodes_FlowId_DefinitionId",
                table: "automation_flow_nodes",
                columns: new[] { "FlowId", "DefinitionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_runs_FlowId_SourceDefinitionId_SourceOccurrenceId",
                table: "automation_flow_runs",
                columns: new[] { "FlowId", "SourceDefinitionId", "SourceOccurrenceId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_runs_HostId_Status",
                table: "automation_flow_runs",
                columns: new[] { "HostId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flows_HostId_IsEnabled",
                table: "automation_flows",
                columns: new[] { "HostId", "IsEnabled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_node_runs_RunId_NodeId",
                table: "automation_node_runs",
                columns: new[] { "RunId", "NodeId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_node_runs_Status_AvailableAtUtc",
                table: "automation_node_runs",
                columns: new[] { "Status", "AvailableAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "automation_flow_edges");

            migrationBuilder.DropTable(name: "automation_flow_nodes");

            migrationBuilder.DropTable(name: "automation_node_runs");

            migrationBuilder.DropTable(name: "automation_flow_runs");

            migrationBuilder.DropTable(name: "automation_flows");

            migrationBuilder.DropColumn(name: "AutomationGeneration", table: "hosts");
        }
    }
}
