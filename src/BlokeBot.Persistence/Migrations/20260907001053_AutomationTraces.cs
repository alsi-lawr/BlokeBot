using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationTraces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_traces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductionRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ByteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Truncation = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_traces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_traces_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_trace_events",
                columns: table => new
                {
                    TraceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    EventJson = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_trace_events",
                        x => new { x.TraceId, x.Sequence }
                    );
                    table.ForeignKey(
                        name: "FK_automation_trace_events_automation_traces_TraceId",
                        column: x => x.TraceId,
                        principalTable: "automation_traces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_traces_ExpiresAtUtc",
                table: "automation_traces",
                column: "ExpiresAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_traces_HostId_CreatedAtUtc",
                table: "automation_traces",
                columns: new[] { "HostId", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_traces_ProductionRunId",
                table: "automation_traces",
                column: "ProductionRunId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "automation_trace_events");

            migrationBuilder.DropTable(name: "automation_traces");
        }
    }
}
