using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationSubflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_subflows",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastRevision = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_subflows", x => new { x.HostId, x.Id });
                    table.ForeignKey(
                        name: "FK_automation_subflows_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_subflow_revisions",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_subflow_revisions",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_automation_subflow_revisions_automation_subflows_HostId_SubflowId",
                        columns: x => new { x.HostId, x.SubflowId },
                        principalTable: "automation_subflows",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_subflow_callers",
                columns: table => new
                {
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_subflow_callers", x => x.NodeId);
                    table.ForeignKey(
                        name: "FK_automation_subflow_callers_automation_flow_nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "automation_flow_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_automation_subflow_callers_automation_subflow_revisions_HostId_RevisionId",
                        columns: x => new { x.HostId, x.RevisionId },
                        principalTable: "automation_subflow_revisions",
                        principalColumns: new[] { "HostId", "Id" }
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_subflow_revision_references",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CallerRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_subflow_revision_references",
                        x => new
                        {
                            x.HostId,
                            x.CallerRevisionId,
                            x.NodeId,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_automation_subflow_revision_references_automation_subflow_revisions_HostId_CallerRevisionId",
                        columns: x => new { x.HostId, x.CallerRevisionId },
                        principalTable: "automation_subflow_revisions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_automation_subflow_revision_references_automation_subflow_revisions_HostId_RevisionId",
                        columns: x => new { x.HostId, x.RevisionId },
                        principalTable: "automation_subflow_revisions",
                        principalColumns: new[] { "HostId", "Id" }
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_subflow_run_references",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_subflow_run_references",
                        x => new { x.RunId, x.RevisionId }
                    );
                    table.ForeignKey(
                        name: "FK_automation_subflow_run_references_automation_flow_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "automation_flow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_automation_subflow_run_references_automation_subflow_revisions_HostId_RevisionId",
                        columns: x => new { x.HostId, x.RevisionId },
                        principalTable: "automation_subflow_revisions",
                        principalColumns: new[] { "HostId", "Id" }
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_subflow_callers_HostId_RevisionId",
                table: "automation_subflow_callers",
                columns: new[] { "HostId", "RevisionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_subflow_revision_references_HostId_RevisionId",
                table: "automation_subflow_revision_references",
                columns: new[] { "HostId", "RevisionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_subflow_revisions_HostId_SubflowId_Revision",
                table: "automation_subflow_revisions",
                columns: new[] { "HostId", "SubflowId", "Revision" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_subflow_run_references_HostId_RevisionId",
                table: "automation_subflow_run_references",
                columns: new[] { "HostId", "RevisionId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "automation_subflow_callers");

            migrationBuilder.DropTable(name: "automation_subflow_revision_references");

            migrationBuilder.DropTable(name: "automation_subflow_run_references");

            migrationBuilder.DropTable(name: "automation_subflow_revisions");

            migrationBuilder.DropTable(name: "automation_subflows");
        }
    }
}
