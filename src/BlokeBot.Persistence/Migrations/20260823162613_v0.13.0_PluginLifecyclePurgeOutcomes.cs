using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginLifecyclePurgeOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plugin_lifecycle_outcomes",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OutcomeCode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 24,
                        nullable: false
                    ),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    OutcomeDetail = table.Column<string>(
                        type: "TEXT",
                        maxLength: 256,
                        nullable: true
                    ),
                    OutcomeOccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_lifecycle_outcomes", x => x.PluginId);
                    table.CheckConstraint(
                        "CK_plugin_lifecycle_outcomes_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycle_outcomes_OutcomeCode",
                        "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'Purged', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
                    );
                }
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "plugin_lifecycle_outcomes"
                    ("PluginId", "OutcomeCode", "FailureCode", "OutcomeDetail", "OutcomeOccurredAtUtc")
                SELECT
                    "PluginId", "OutcomeCode", "FailureCode", "OutcomeDetail", "OutcomeOccurredAtUtc"
                FROM "plugin_lifecycles"
                WHERE "Phase" = 'Purged';

                DELETE FROM "plugin_lifecycles"
                WHERE "Phase" = 'Purged';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "plugin_lifecycle_outcomes");
        }
    }
}
