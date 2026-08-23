using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginLifecycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plugin_lifecycles",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SelectedVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    SelectedTag = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SelectedGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    ActiveVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ActiveTag = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ActiveOperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActiveGeneration = table.Column<long>(type: "INTEGER", nullable: true),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OperationKind = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false
                    ),
                    FaultedFrom = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    AutomaticRestartConsumed = table.Column<bool>(type: "INTEGER", nullable: false),
                    RestartNotBeforeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_lifecycles", x => x.PluginId);
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_ActiveRuntime",
                        "(\"ActiveVersion\" IS NULL AND \"ActiveTag\" IS NULL AND \"ActiveOperationId\" IS NULL AND \"ActiveGeneration\" IS NULL) OR (\"ActiveVersion\" IS NOT NULL AND \"ActiveTag\" IS NOT NULL AND \"ActiveOperationId\" IS NOT NULL AND \"ActiveGeneration\" > 0)"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_FaultedFrom",
                        "\"FaultedFrom\" IS NULL OR \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Purged', 'Faulted')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_OperationKind",
                        "\"OperationKind\" IN ('Activate', 'Remove', 'Purge', 'Restart')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_OutcomeCode",
                        "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'Purged', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_Phase",
                        "\"Phase\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Purged', 'Faulted')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_SelectedGeneration",
                        "\"SelectedGeneration\" > 0"
                    );
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "plugin_lifecycles");
        }
    }
}
