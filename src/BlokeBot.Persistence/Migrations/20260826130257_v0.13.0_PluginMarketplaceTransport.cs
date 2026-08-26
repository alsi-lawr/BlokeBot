using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginMarketplaceTransport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "plugin_lifecycle_outcomes");

            migrationBuilder.Sql(
                """
                UPDATE "plugin_lifecycles"
                SET "Phase" = CASE
                        WHEN "Phase" IN ('Removed', 'Purging') THEN 'Removing'
                        ELSE "Phase"
                    END,
                    "FaultedFrom" = CASE
                        WHEN "FaultedFrom" = 'Purging' THEN 'Removing'
                        ELSE "FaultedFrom"
                    END,
                    "OperationKind" = CASE
                        WHEN "OperationKind" = 'Purge' THEN 'Remove'
                        ELSE "OperationKind"
                    END,
                    "OutcomeCode" = CASE
                        WHEN "OutcomeCode" = 'Purged' THEN 'Removed'
                        ELSE "OutcomeCode"
                    END,
                    "FailureCode" = CASE
                        WHEN "FailureCode" = 'PurgeFailed' THEN 'RemovalFailed'
                        ELSE "FailureCode"
                    END
                WHERE "Phase" IN ('Removed', 'Purging')
                    OR "FaultedFrom" = 'Purging'
                    OR "OperationKind" = 'Purge'
                    OR "OutcomeCode" = 'Purged'
                    OR "FailureCode" = 'PurgeFailed';
                """
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FailureCode",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_OperationKind",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_OutcomeCode",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_Phase",
                table: "plugin_lifecycles"
            );

            migrationBuilder.CreateTable(
                name: "plugin_marketplace_catalog_state",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceETag = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1024,
                        nullable: true
                    ),
                    SourceModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_marketplace_catalog_state", x => x.Id);
                    table.CheckConstraint(
                        "CK_plugin_marketplace_catalog_state_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('DownloadFailed', 'MalformedCatalog', 'UnsupportedSchema', 'InvalidEntry', 'DuplicateRelease')"
                    );
                    table.CheckConstraint("CK_plugin_marketplace_catalog_state_Id", "\"Id\" = 1");
                    table.CheckConstraint(
                        "CK_plugin_marketplace_catalog_state_Success",
                        "(\"SchemaVersion\" IS NULL AND \"FetchedAtUtc\" IS NULL AND \"SourceETag\" IS NULL AND \"SourceModifiedAtUtc\" IS NULL) OR (\"SchemaVersion\" = 1 AND \"FetchedAtUtc\" IS NOT NULL)"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_marketplace_receipts",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DeclaredVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    MutableTag = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OutcomeCode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    SafeDetail = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_marketplace_receipts", x => x.PluginId);
                    table.CheckConstraint(
                        "CK_plugin_marketplace_receipts_Operation",
                        "\"Operation\" IN ('Install', 'Update', 'Remove', 'Restart')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_marketplace_receipts_Release",
                        "(\"DeclaredVersion\" IS NULL AND \"MutableTag\" IS NULL) OR (\"DeclaredVersion\" IS NOT NULL AND \"MutableTag\" IS NOT NULL)"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_marketplace_catalog_entries",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeclaredVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    SnapshotId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IconUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RepositoryUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: false
                    ),
                    PackagePath = table.Column<string>(
                        type: "TEXT",
                        maxLength: 240,
                        nullable: false
                    ),
                    CompatibilityBlokeBot = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    CompatibilityPluginApi = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    CompatibilityLua = table.Column<string>(
                        type: "TEXT",
                        maxLength: 8,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_plugin_marketplace_catalog_entries",
                        x => new
                        {
                            x.PluginId,
                            x.DeclaredVersion,
                            x.MutableTag,
                        }
                    );
                    table.CheckConstraint(
                        "CK_plugin_marketplace_catalog_entries_SnapshotId",
                        "\"SnapshotId\" = 1"
                    );
                    table.ForeignKey(
                        name: "FK_plugin_marketplace_catalog_entries_plugin_marketplace_catalog_state_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "plugin_marketplace_catalog_state",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_marketplace_catalog_media",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeclaredVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_plugin_marketplace_catalog_media",
                        x => new
                        {
                            x.PluginId,
                            x.DeclaredVersion,
                            x.MutableTag,
                            x.Position,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_plugin_marketplace_catalog_media_plugin_marketplace_catalog_entries_PluginId_DeclaredVersion_MutableTag",
                        columns: x => new
                        {
                            x.PluginId,
                            x.DeclaredVersion,
                            x.MutableTag,
                        },
                        principalTable: "plugin_marketplace_catalog_entries",
                        principalColumns: new[] { "PluginId", "DeclaredVersion", "MutableTag" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_marketplace_catalog_tags",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeclaredVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_plugin_marketplace_catalog_tags",
                        x => new
                        {
                            x.PluginId,
                            x.DeclaredVersion,
                            x.MutableTag,
                            x.Position,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_plugin_marketplace_catalog_tags_plugin_marketplace_catalog_entries_PluginId_DeclaredVersion_MutableTag",
                        columns: x => new
                        {
                            x.PluginId,
                            x.DeclaredVersion,
                            x.MutableTag,
                        },
                        principalTable: "plugin_marketplace_catalog_entries",
                        principalColumns: new[] { "PluginId", "DeclaredVersion", "MutableTag" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_marketplace_catalog_targets",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DeclaredVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_plugin_marketplace_catalog_targets",
                        x => new
                        {
                            x.PluginId,
                            x.DeclaredVersion,
                            x.MutableTag,
                            x.Position,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_plugin_marketplace_catalog_targets_plugin_marketplace_catalog_entries_PluginId_DeclaredVersion_MutableTag",
                        columns: x => new
                        {
                            x.PluginId,
                            x.DeclaredVersion,
                            x.MutableTag,
                        },
                        principalTable: "plugin_marketplace_catalog_entries",
                        principalColumns: new[] { "PluginId", "DeclaredVersion", "MutableTag" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FailureCode",
                table: "plugin_lifecycles",
                sql: "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles",
                sql: "(\"Phase\" = 'Faulted' AND \"FaultedFrom\" IS NOT NULL AND \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing')) OR (\"Phase\" <> 'Faulted' AND \"FaultedFrom\" IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_OperationKind",
                table: "plugin_lifecycles",
                sql: "\"OperationKind\" IN ('Activate', 'Remove', 'Restart')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_OutcomeCode",
                table: "plugin_lifecycles",
                sql: "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_Phase",
                table: "plugin_lifecycles",
                sql: "\"Phase\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Faulted')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_marketplace_catalog_entries_SnapshotId",
                table: "plugin_marketplace_catalog_entries",
                column: "SnapshotId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_media");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_tags");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_targets");

            migrationBuilder.DropTable(name: "plugin_marketplace_receipts");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_entries");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_state");

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FailureCode",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_OperationKind",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_OutcomeCode",
                table: "plugin_lifecycles"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_plugin_lifecycles_Phase",
                table: "plugin_lifecycles"
            );

            migrationBuilder.CreateTable(
                name: "plugin_lifecycle_outcomes",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    OutcomeCode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 24,
                        nullable: false
                    ),
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
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycle_outcomes_OutcomeCode",
                        "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'Purged', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
                    );
                }
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FailureCode",
                table: "plugin_lifecycles",
                sql: "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_FaultedFrom",
                table: "plugin_lifecycles",
                sql: "(\"Phase\" = 'Faulted' AND \"FaultedFrom\" IS NOT NULL AND \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Purging')) OR (\"Phase\" <> 'Faulted' AND \"FaultedFrom\" IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_OperationKind",
                table: "plugin_lifecycles",
                sql: "\"OperationKind\" IN ('Activate', 'Remove', 'Purge', 'Restart')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_OutcomeCode",
                table: "plugin_lifecycles",
                sql: "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'Purged', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_plugin_lifecycles_Phase",
                table: "plugin_lifecycles",
                sql: "\"Phase\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Faulted')"
            );
        }
    }
}
