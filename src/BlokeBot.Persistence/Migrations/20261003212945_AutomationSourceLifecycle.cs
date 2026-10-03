using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationSourceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_countdowns",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    OccurrenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutomationGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_countdowns", x => new { x.HostId, x.Name });
                    table.ForeignKey(
                        name: "FK_automation_countdowns_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_goal_observations",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CurrentAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnded = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_goal_observations",
                        x => new { x.HostId, x.GoalId }
                    );
                    table.ForeignKey(
                        name: "FK_automation_goal_observations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_source_admissions",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    AcceptEventsAfterUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_source_admissions", x => x.HostId);
                    table.ForeignKey(
                        name: "FK_automation_source_admissions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_stream_observations",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    StreamId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SuppressUptimeBeforeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_stream_observations", x => x.HostId);
                    table.ForeignKey(
                        name: "FK_automation_stream_observations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_goal_milestones",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_goal_milestones",
                        x => new
                        {
                            x.HostId,
                            x.GoalId,
                            x.Amount,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_automation_goal_milestones_automation_goal_observations_HostId_GoalId",
                        columns: x => new { x.HostId, x.GoalId },
                        principalTable: "automation_goal_observations",
                        principalColumns: new[] { "HostId", "GoalId" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_seen_viewers",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_seen_viewers",
                        x => new { x.HostId, x.ViewerId }
                    );
                    table.ForeignKey(
                        name: "FK_automation_seen_viewers_automation_stream_observations_HostId",
                        column: x => x.HostId,
                        principalTable: "automation_stream_observations",
                        principalColumn: "HostId",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "automation_countdowns");

            migrationBuilder.DropTable(name: "automation_goal_milestones");

            migrationBuilder.DropTable(name: "automation_seen_viewers");

            migrationBuilder.DropTable(name: "automation_source_admissions");

            migrationBuilder.DropTable(name: "automation_goal_observations");

            migrationBuilder.DropTable(name: "automation_stream_observations");
        }
    }
}
