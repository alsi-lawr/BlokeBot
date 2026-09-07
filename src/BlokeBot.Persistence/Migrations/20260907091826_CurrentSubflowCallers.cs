using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurrentSubflowCallers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM automation_subflow_callers;");
            migrationBuilder.DropForeignKey(
                name: "FK_automation_subflow_callers_automation_subflow_revisions_HostId_RevisionId",
                table: "automation_subflow_callers"
            );

            migrationBuilder.DropTable(name: "automation_subflow_revision_references");

            migrationBuilder.RenameColumn(
                name: "RevisionId",
                table: "automation_subflow_callers",
                newName: "SubflowId"
            );

            migrationBuilder.RenameIndex(
                name: "IX_automation_subflow_callers_HostId_RevisionId",
                table: "automation_subflow_callers",
                newName: "IX_automation_subflow_callers_HostId_SubflowId"
            );

            migrationBuilder.CreateTable(
                name: "automation_subflow_nested_callers",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CallerSubflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_subflow_nested_callers",
                        x => new
                        {
                            x.HostId,
                            x.CallerSubflowId,
                            x.NodeId,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_automation_subflow_nested_callers_automation_subflows_HostId_CallerSubflowId",
                        columns: x => new { x.HostId, x.CallerSubflowId },
                        principalTable: "automation_subflows",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_automation_subflow_nested_callers_automation_subflows_HostId_SubflowId",
                        columns: x => new { x.HostId, x.SubflowId },
                        principalTable: "automation_subflows",
                        principalColumns: new[] { "HostId", "Id" }
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_subflow_nested_callers_HostId_SubflowId",
                table: "automation_subflow_nested_callers",
                columns: new[] { "HostId", "SubflowId" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_automation_subflow_callers_automation_subflows_HostId_SubflowId",
                table: "automation_subflow_callers",
                columns: new[] { "HostId", "SubflowId" },
                principalTable: "automation_subflows",
                principalColumns: new[] { "HostId", "Id" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM automation_subflow_callers;");
            migrationBuilder.DropForeignKey(
                name: "FK_automation_subflow_callers_automation_subflows_HostId_SubflowId",
                table: "automation_subflow_callers"
            );

            migrationBuilder.DropTable(name: "automation_subflow_nested_callers");

            migrationBuilder.RenameColumn(
                name: "SubflowId",
                table: "automation_subflow_callers",
                newName: "RevisionId"
            );

            migrationBuilder.RenameIndex(
                name: "IX_automation_subflow_callers_HostId_SubflowId",
                table: "automation_subflow_callers",
                newName: "IX_automation_subflow_callers_HostId_RevisionId"
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

            migrationBuilder.CreateIndex(
                name: "IX_automation_subflow_revision_references_HostId_RevisionId",
                table: "automation_subflow_revision_references",
                columns: new[] { "HostId", "RevisionId" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_automation_subflow_callers_automation_subflow_revisions_HostId_RevisionId",
                table: "automation_subflow_callers",
                columns: new[] { "HostId", "RevisionId" },
                principalTable: "automation_subflow_revisions",
                principalColumns: new[] { "HostId", "Id" }
            );
        }
    }
}
