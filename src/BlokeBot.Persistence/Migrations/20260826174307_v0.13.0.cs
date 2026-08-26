using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_bounty_moderation_audit_Action",
                table: "bounty_moderation_audit"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_ResultCode",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_State",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_Status",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.AddColumn<string>(
                name: "IssuesJson",
                table: "configuration_activations",
                type: "TEXT",
                maxLength: 4096,
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE configuration_activations
                SET IssuesJson = json_array(
                    json_object(
                        'Code', FailureCode,
                        'Reason', 'A previous automatic activation failed. Retry automatic activation.'
                    )
                )
                WHERE FailureCode IS NOT NULL;
                """
            );

            migrationBuilder.DropColumn(name: "FailureCode", table: "configuration_activations");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAccessKeyRegeneration",
                table: "overlay_instances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOccurredAtUtc",
                table: "durable_alerts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                table: "durable_alerts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.Sql(
                """
                UPDATE "durable_alerts"
                SET "LastOccurredAtUtc" = "CreatedAtUtc";
                """
            );

            migrationBuilder.AddColumn<string>(
                name: "UnavailableReason",
                table: "automation_flows",
                type: "TEXT",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PluginProvenanceJson",
                table: "automation_flow_nodes",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "plugin_automation_instantiations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnableOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FeatureId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PluginVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ManifestVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FlowId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Diagnostic = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_automation_instantiations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plugin_automation_instantiations_automation_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "automation_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_plugin_automation_instantiations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_feature_configurations",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FeatureId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_plugin_feature_configurations",
                        x => new
                        {
                            x.PluginId,
                            x.FeatureId,
                            x.HostId,
                        }
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_configurations_Revision",
                        "\"Revision\" >= 0"
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_configurations_ValuesJson",
                        "json_valid(\"ValuesJson\") AND json_type(\"ValuesJson\") = 'array' AND length(CAST(\"ValuesJson\" AS BLOB)) <= 65536"
                    );
                    table.ForeignKey(
                        name: "FK_plugin_feature_configurations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_feature_states",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FeatureId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    LifecycleOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkerGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    FeatureGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    Readiness = table.Column<string>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: true),
                    RecoveryAction = table.Column<string>(type: "TEXT", nullable: true),
                    ReasonDetail = table.Column<string>(
                        type: "TEXT",
                        maxLength: 256,
                        nullable: true
                    ),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_plugin_feature_states",
                        x => new
                        {
                            x.PluginId,
                            x.FeatureId,
                            x.HostId,
                        }
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_states_Generations",
                        "\"WorkerGeneration\" > 0 AND \"FeatureGeneration\" > 0"
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_states_Readiness",
                        "\"Readiness\" IN ('Disabled', 'EnabledDegraded', 'Ready')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_states_Reason",
                        "(\"Readiness\" = 'EnabledDegraded' AND \"ReasonCode\" IS NOT NULL AND \"RecoveryAction\" IS NOT NULL AND \"ReasonDetail\" IS NOT NULL) OR (\"Readiness\" <> 'EnabledDegraded' AND \"ReasonCode\" IS NULL AND \"RecoveryAction\" IS NULL AND \"ReasonDetail\" IS NULL)"
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_states_ReasonCode",
                        "\"ReasonCode\" IS NULL OR \"ReasonCode\" IN ('MissingScopes', 'ReconciliationPending', 'ReconciliationFailed')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_states_ReasonDetail",
                        "\"ReasonDetail\" IS NULL OR length(trim(\"ReasonDetail\")) BETWEEN 1 AND 256"
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_states_RecoveryAction",
                        "\"RecoveryAction\" IS NULL OR \"RecoveryAction\" IN ('ReconnectTwitch', 'Retry')"
                    );
                    table.CheckConstraint("CK_plugin_feature_states_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_plugin_feature_states_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_installation_configurations",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_installation_configurations", x => x.PluginId);
                    table.CheckConstraint(
                        "CK_plugin_installation_configurations_Revision",
                        "\"Revision\" >= 0"
                    );
                    table.CheckConstraint(
                        "CK_plugin_installation_configurations_ValuesJson",
                        "json_valid(\"ValuesJson\") AND json_type(\"ValuesJson\") = 'array' AND length(CAST(\"ValuesJson\" AS BLOB)) <= 65536"
                    );
                }
            );

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
                    SelectedPackageOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SelectedGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    ActiveVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ActiveTag = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ActiveOperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActivePackageOperationId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                        "(\"ActiveVersion\" IS NULL AND \"ActiveTag\" IS NULL AND \"ActiveOperationId\" IS NULL AND \"ActivePackageOperationId\" IS NULL AND \"ActiveGeneration\" IS NULL) OR (\"ActiveVersion\" IS NOT NULL AND \"ActiveTag\" IS NOT NULL AND \"ActiveOperationId\" IS NOT NULL AND \"ActivePackageOperationId\" IS NOT NULL AND \"ActiveGeneration\" > 0)"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_FaultedFrom",
                        "(\"Phase\" = 'Faulted' AND \"FaultedFrom\" IS NOT NULL AND \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing')) OR (\"Phase\" <> 'Faulted' AND \"FaultedFrom\" IS NULL)"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_FaultShutdown",
                        "\"Phase\" <> 'Faulted' OR \"ActiveOperationId\" IS NULL OR (\"FaultedFrom\" = 'Active' AND \"ActiveVersion\" = \"SelectedVersion\" AND \"ActiveTag\" = \"SelectedTag\" AND \"ActiveOperationId\" = \"OperationId\" AND \"ActivePackageOperationId\" = \"SelectedPackageOperationId\" AND \"ActiveGeneration\" = \"SelectedGeneration\")"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_OperationKind",
                        "\"OperationKind\" IN ('Activate', 'Remove', 'Replace', 'Restart')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_OutcomeCode",
                        "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_Phase",
                        "\"Phase\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Faulted')"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_SelectedGeneration",
                        "\"SelectedGeneration\" > 0"
                    );
                    table.CheckConstraint(
                        "CK_plugin_lifecycles_SelectedPackageOperation",
                        "\"SelectedPackageOperationId\" <> '00000000-0000-0000-0000-000000000000'"
                    );
                }
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
                name: "plugin_feature_secrets",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FeatureId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SettingId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProtectedValue = table.Column<byte[]>(type: "BLOB", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_plugin_feature_secrets",
                        x => new
                        {
                            x.PluginId,
                            x.FeatureId,
                            x.HostId,
                            x.SettingId,
                        }
                    );
                    table.CheckConstraint(
                        "CK_plugin_feature_secrets_ProtectedValue",
                        "length(\"ProtectedValue\") > 0 AND length(\"ProtectedValue\") <= 32768"
                    );
                    table.ForeignKey(
                        name: "FK_plugin_feature_secrets_plugin_feature_configurations_PluginId_FeatureId_HostId",
                        columns: x => new
                        {
                            x.PluginId,
                            x.FeatureId,
                            x.HostId,
                        },
                        principalTable: "plugin_feature_configurations",
                        principalColumns: new[] { "PluginId", "FeatureId", "HostId" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_installation_secrets",
                columns: table => new
                {
                    PluginId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SettingId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProtectedValue = table.Column<byte[]>(type: "BLOB", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_plugin_installation_secrets",
                        x => new { x.PluginId, x.SettingId }
                    );
                    table.CheckConstraint(
                        "CK_plugin_installation_secrets_ProtectedValue",
                        "length(\"ProtectedValue\") > 0 AND length(\"ProtectedValue\") <= 32768"
                    );
                    table.ForeignKey(
                        name: "FK_plugin_installation_secrets_plugin_installation_configurations_PluginId",
                        column: x => x.PluginId,
                        principalTable: "plugin_installation_configurations",
                        principalColumn: "PluginId",
                        onDelete: ReferentialAction.Cascade
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
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history",
                sql: "ShoutoutOutcome IN ('Cooldown', 'Deduplicated', 'NotConfigured', 'NotEligible', 'Queued', 'Rejected', 'Sent', 'Suppressed')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations",
                sql: "Status IN ('Complete', 'Failed', 'ManualFollowUp', 'Pending', 'Processing')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_bounty_moderation_audit_Action",
                table: "bounty_moderation_audit",
                sql: "Action IN ('Accepted', 'Cancelled', 'Completed', 'Created', 'Expired', 'Extended', 'Failed', 'FundingOpened', 'PauseAdjusted', 'Rejected')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_ResultCode",
                table: "automatic_raid_shoutout_outcomes",
                sql: "ResultCode IS NULL OR ResultCode IN ('Ambiguous', 'AuthorityRequired', 'Cooldown', 'Delivered', 'Invalid', 'NotReady', 'PartialFailure', 'Queued', 'RateLimited', 'Rejected', 'RuntimeMessageTooLong', 'Unexpected')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_State",
                table: "automatic_raid_shoutout_outcomes",
                sql: "(Status = 'Processing' AND ResultCode IS NULL AND CompletedAtUtc IS NULL) OR (Status = 'Queued' AND ResultCode = 'Queued' AND CompletedAtUtc IS NULL) OR (Status = 'Delivered' AND ResultCode = 'Delivered' AND CompletedAtUtc IS NOT NULL) OR (Status = 'NotDelivered' AND ResultCode IS NOT NULL AND ResultCode NOT IN ('Queued', 'Delivered', 'Ambiguous') AND CompletedAtUtc IS NOT NULL) OR (Status = 'Ambiguous' AND ResultCode = 'Ambiguous' AND CompletedAtUtc IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_Status",
                table: "automatic_raid_shoutout_outcomes",
                sql: "Status IN ('Ambiguous', 'Delivered', 'NotDelivered', 'Processing', 'Queued')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_EnableOperationId_TemplateId",
                table: "plugin_automation_instantiations",
                columns: new[] { "EnableOperationId", "TemplateId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_FlowId",
                table: "plugin_automation_instantiations",
                column: "FlowId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_HostId",
                table: "plugin_automation_instantiations",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_PluginId_FeatureId_HostId_TemplateId_TemplateHash",
                table: "plugin_automation_instantiations",
                columns: new[] { "PluginId", "FeatureId", "HostId", "TemplateId", "TemplateHash" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_feature_configurations_HostId",
                table: "plugin_feature_configurations",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_feature_states_HostId",
                table: "plugin_feature_states",
                column: "HostId"
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
            migrationBuilder.DropTable(name: "plugin_automation_instantiations");

            migrationBuilder.DropTable(name: "plugin_feature_secrets");

            migrationBuilder.DropTable(name: "plugin_feature_states");

            migrationBuilder.DropTable(name: "plugin_installation_secrets");

            migrationBuilder.DropTable(name: "plugin_lifecycles");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_media");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_tags");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_targets");

            migrationBuilder.DropTable(name: "plugin_marketplace_receipts");

            migrationBuilder.DropTable(name: "plugin_feature_configurations");

            migrationBuilder.DropTable(name: "plugin_installation_configurations");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_entries");

            migrationBuilder.DropTable(name: "plugin_marketplace_catalog_state");

            migrationBuilder.DropCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_bounty_moderation_audit_Action",
                table: "bounty_moderation_audit"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_ResultCode",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_State",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_Status",
                table: "automatic_raid_shoutout_outcomes"
            );

            migrationBuilder.DropColumn(
                name: "RequiresAccessKeyRegeneration",
                table: "overlay_instances"
            );

            migrationBuilder.DropColumn(name: "LastOccurredAtUtc", table: "durable_alerts");

            migrationBuilder.DropColumn(name: "OccurrenceCount", table: "durable_alerts");

            migrationBuilder.DropColumn(name: "IssuesJson", table: "configuration_activations");

            migrationBuilder.DropColumn(name: "UnavailableReason", table: "automation_flows");

            migrationBuilder.DropColumn(
                name: "PluginProvenanceJson",
                table: "automation_flow_nodes"
            );

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "configuration_activations",
                type: "TEXT",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_raid_collaboration_history_ShoutoutOutcome",
                table: "raid_collaboration_history",
                sql: "ShoutoutOutcome IN ('Cooldown', 'Deduplicated', 'NotConfigured', 'NotEligible', 'Rejected', 'Sent', 'Suppressed')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations",
                sql: "Status IN ('Complete', 'Failed', 'Pending', 'Processing')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_bounty_moderation_audit_Action",
                table: "bounty_moderation_audit",
                sql: "Action IN ('Accepted', 'Cancelled', 'Completed', 'Created', 'Expired', 'Extended', 'Failed', 'FundingOpened', 'Rejected')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_ResultCode",
                table: "automatic_raid_shoutout_outcomes",
                sql: "ResultCode IS NULL OR ResultCode IN ('Ambiguous', 'AuthorityRequired', 'Cooldown', 'Delivered', 'Invalid', 'NotReady', 'PartialFailure', 'RateLimited', 'Rejected', 'RuntimeMessageTooLong', 'Unexpected')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_State",
                table: "automatic_raid_shoutout_outcomes",
                sql: "(Status = 'Processing' AND ResultCode IS NULL AND CompletedAtUtc IS NULL) OR (Status = 'Delivered' AND ResultCode IS NOT NULL AND ResultCode = 'Delivered' AND CompletedAtUtc IS NOT NULL) OR (Status = 'NotDelivered' AND ResultCode IS NOT NULL AND ResultCode NOT IN ('Delivered', 'Ambiguous') AND CompletedAtUtc IS NOT NULL) OR (Status = 'Ambiguous' AND ResultCode IS NOT NULL AND ResultCode = 'Ambiguous' AND CompletedAtUtc IS NOT NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_automatic_raid_shoutout_outcomes_Status",
                table: "automatic_raid_shoutout_outcomes",
                sql: "Status IN ('Ambiguous', 'Delivered', 'NotDelivered', 'Processing')"
            );
        }
    }
}
