using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_PluginFeatureConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "plugin_feature_secrets");

            migrationBuilder.DropTable(name: "plugin_feature_states");

            migrationBuilder.DropTable(name: "plugin_installation_secrets");

            migrationBuilder.DropTable(name: "plugin_feature_configurations");

            migrationBuilder.DropTable(name: "plugin_installation_configurations");
        }
    }
}
