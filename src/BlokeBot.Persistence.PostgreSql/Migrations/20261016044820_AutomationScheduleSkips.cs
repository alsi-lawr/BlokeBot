using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AutomationScheduleSkips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_schedule_skips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_schedule_skips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_schedule_skips_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_automation_schedule_skips_ExpiresAtUtc",
                table: "automation_schedule_skips",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_automation_schedule_skips_HostId_FlowId_SourceNodeId_Observ~",
                table: "automation_schedule_skips",
                columns: new[] { "HostId", "FlowId", "SourceNodeId", "ObservedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_schedule_skips");
        }
    }
}
