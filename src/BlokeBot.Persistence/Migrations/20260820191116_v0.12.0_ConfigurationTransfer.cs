using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0120_ConfigurationTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH migration_reference(value) AS (
                    SELECT strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                )
                UPDATE custom_announcement_schedules
                SET WeeklyDay = blokebot_weekly_utc_day(
                        WeeklyDay,
                        WeeklyTime,
                        (SELECT TimeZoneId FROM hosts WHERE hosts.Id = custom_announcement_schedules.HostId),
                        (SELECT value FROM migration_reference)
                    ),
                    WeeklyTime = blokebot_weekly_utc_time(
                        WeeklyDay,
                        WeeklyTime,
                        (SELECT TimeZoneId FROM hosts WHERE hosts.Id = custom_announcement_schedules.HostId),
                        (SELECT value FROM migration_reference)
                    )
                WHERE ScheduleType = 'Weekly';
                """
            );

            migrationBuilder.CreateTable(
                name: "configuration_activations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnabledChanges = table.Column<long>(type: "INTEGER", nullable: false),
                    DisabledChanges = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_activations", x => x.Id);
                    table.CheckConstraint(
                        "CK_configuration_activations_Status",
                        "Status IN ('Complete', 'Failed', 'Pending', 'Processing')"
                    );
                    table.ForeignKey(
                        name: "FK_configuration_activations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "configuration_import_audits",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false),
                    ActorTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    SourceFormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SummaryJson = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_import_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_import_audits_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_configuration_activations_HostId_Status",
                table: "configuration_activations",
                columns: new[] { "HostId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_configuration_import_audits_HostId_OccurredAtUtc",
                table: "configuration_import_audits",
                columns: new[] { "HostId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_configuration_import_audits_OperationId",
                table: "configuration_import_audits",
                column: "OperationId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "configuration_activations");

            migrationBuilder.DropTable(name: "configuration_import_audits");
        }
    }
}
