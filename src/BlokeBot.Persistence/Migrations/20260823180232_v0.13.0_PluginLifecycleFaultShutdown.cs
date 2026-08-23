using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginLifecycleFaultShutdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FailureCode",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycle_outcomes_FailureCode",
                table: "plugin_lifecycle_outcomes"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FailureCode",
                table: "plugin_lifecycles",
                sql: "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles",
                sql: "\"FaultedFrom\" IS NULL OR \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Faulted')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles",
                sql: "\"Phase\" <> 'Faulted' OR \"ActiveOperationId\" IS NULL OR \"FaultedFrom\" = 'Active'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycle_outcomes_FailureCode",
                table: "plugin_lifecycle_outcomes",
                sql: "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FailureCode",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultShutdown",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycle_outcomes_FailureCode",
                table: "plugin_lifecycle_outcomes"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FailureCode",
                table: "plugin_lifecycles",
                sql: "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles",
                sql: "\"FaultedFrom\" IS NULL OR \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Purged', 'Faulted')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycle_outcomes_FailureCode",
                table: "plugin_lifecycle_outcomes",
                sql: "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
            );
        }
    }
}
