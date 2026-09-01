using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlokeBot.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class _20260901_v0_14_0_Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collectives",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collectives", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "hosts",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ProfileImageUrl = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    ChannelBotAuthorizedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ChannelBotAuthorizedScopes = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    BotRuntimeState = table.Column<int>(type: "integer", nullable: false),
                    BotRuntimeStateChangedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    EnabledFeatures = table.Column<long>(
                        type: "bigint",
                        nullable: false,
                        defaultValue: 0L
                    ),
                    BountiesPausedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CommunityProgressionPausedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CommunityProgressionAcceptEventsAfterUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    BingoPausedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    BingoAcceptEventsAfterUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CompetitionsPausedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CompetitionsAcceptWorkAfterUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    RaidCollaborationPausedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    RaidCollaborationAcceptEventsAfterUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    BlokeRaidPausedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    BlokeRaidAcceptWorkAfterUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CollectivesPausedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CollectivesAcceptWorkAfterUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ViewerPassportContinuityGeneration = table.Column<int>(
                        type: "integer",
                        nullable: false
                    ),
                    AutomationGeneration = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false,
                        defaultValue: "UTC"
                    ),
                    StartupMessageEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    StartupMessageText = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    CommandsAliasesConfigured = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    CommandsDefaultConflictAlias = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hosts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_media_documents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", nullable: false),
                    ContentType = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    State = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    LegacyHostId = table.Column<int>(type: "integer", nullable: true),
                    LegacyStorageKey = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    OrphanedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_media_documents", x => x.Id);
                    table.CheckConstraint(
                        "CK_overlay_media_documents_ContentType",
                        "\"ContentType\" LIKE 'image/%' OR \"ContentType\" LIKE 'audio/%' OR \"ContentType\" LIKE 'video/%'"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_documents_Legacy",
                        "(\"LegacyHostId\" IS NULL AND \"LegacyStorageKey\" IS NULL) OR (\"LegacyHostId\" IS NOT NULL AND length(\"LegacyStorageKey\") = 32)"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_documents_Length",
                        "\"ByteLength\" > 0"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_documents_State",
                        "\"State\" IN ('available', 'orphaned', 'publishing', 'unavailable')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_documents_StorageKey",
                        "length(\"StorageKey\") = 32"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_installation_configurations",
                columns: table => new
                {
                    PluginId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ValuesJson = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
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
                        "jsonb_typeof(\"ValuesJson\"::jsonb) = 'array' AND octet_length(\"ValuesJson\") <= 65536"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_lifecycles",
                columns: table => new
                {
                    PluginId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SelectedVersion = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SelectedTag = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SelectedPackageOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedGeneration = table.Column<long>(type: "bigint", nullable: false),
                    ActiveVersion = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ActiveTag = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ActiveOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivePackageOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveGeneration = table.Column<long>(type: "bigint", nullable: true),
                    Phase = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    OperationKind = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    FaultedFrom = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    AutomaticRestartConsumed = table.Column<bool>(type: "boolean", nullable: false),
                    RestartNotBeforeUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OutcomeCode = table.Column<string>(
                        type: "character varying(24)",
                        maxLength: 24,
                        nullable: false
                    ),
                    FailureCode = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: true
                    ),
                    OutcomeDetail = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    OutcomeOccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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
                    Id = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastAttemptAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    SourceETag = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: true
                    ),
                    SourceModifiedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    FailureCode = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_marketplace_catalog_state", x => x.Id);
                    table.CheckConstraint(
                        "CK_plugin_marketplace_catalog_state_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('DownloadFailed', 'RepositoryInvalid', 'InvalidManifest', 'DuplicatePlugin')"
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
                    PluginId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Operation = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    DeclaredVersion = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    MutableTag = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    OutcomeCode = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    SafeDetail = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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
                name: "public_chat_outbox",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    Channel = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Message = table.Column<string>(type: "text", nullable: true),
                    DeduplicationKey = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    NextAttemptAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    SafePreSendFailureCount = table.Column<int>(type: "integer", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimSlot = table.Column<int>(type: "integer", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    SendStartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    FailurePhase = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    FailureType = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    RejectionCode = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_chat_outbox", x => x.Id);
                    table.CheckConstraint(
                        "CK_public_chat_outbox_AttemptCount",
                        "\"AttemptCount\" >= 0"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_Channel",
                        "length(trim(\"Channel\")) > 0"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_DeduplicationKey",
                        "\"DeduplicationKey\" IS NULL OR length(\"DeduplicationKey\") = 64"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_FailurePhase",
                        "\"FailurePhase\" IS NULL OR \"FailurePhase\" IN ('Preparation', 'Send')"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_SafePreSendFailureCount",
                        "\"SafePreSendFailureCount\" >= 0"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_State",
                        "(\"Status\" = 'Pending' AND length(\"Message\") > 0 AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"AttemptCount\" = 0 AND \"SafePreSendFailureCount\" = 0 AND length(\"DeduplicationKey\") = 64 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"FailurePhase\" IS NULL AND \"FailureType\" IS NULL AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL) OR (\"Status\" = 'Claimed' AND length(\"Message\") > 0 AND \"ClaimToken\" IS NOT NULL AND \"ClaimSlot\" = 1 AND \"ClaimExpiresAtUtc\" IS NOT NULL AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"AttemptCount\" = 0 AND length(\"DeduplicationKey\") = 64 AND \"NextAttemptAtUtc\" IS NOT NULL AND ((\"SafePreSendFailureCount\" = 0 AND \"FailurePhase\" IS NULL AND \"FailureType\" IS NULL AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL) OR (\"SafePreSendFailureCount\" > 0 AND \"FailurePhase\" = 'Preparation' AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL))) OR (\"Status\" = 'Sending' AND length(\"Message\") > 0 AND \"ClaimToken\" IS NOT NULL AND \"ClaimSlot\" = 1 AND \"ClaimExpiresAtUtc\" IS NOT NULL AND \"SendStartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL AND \"AttemptCount\" > 0 AND length(\"DeduplicationKey\") = 64 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"FailurePhase\" IS NULL AND \"FailureType\" IS NULL AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL) OR (\"Status\" = 'SafePreSendTransient' AND length(\"Message\") > 0 AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"AttemptCount\" = 0 AND length(\"DeduplicationKey\") = 64 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"SafePreSendFailureCount\" > 0 AND \"FailurePhase\" = 'Preparation' AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL) OR (\"Status\" = 'SafePreSendExhausted' AND \"Message\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"AttemptCount\" = 0 AND \"SafePreSendFailureCount\" > 0 AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL AND \"FailurePhase\" = 'Preparation' AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL) OR (\"Status\" IN ('MissingChannel', 'MissingBot') AND \"Message\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"AttemptCount\" = 0 AND \"SafePreSendFailureCount\" = 0 AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL AND \"FailurePhase\" = 'Preparation' AND \"FailureType\" IS NULL AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL) OR (\"Status\" = 'Rejected' AND \"Message\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"FailurePhase\" = 'Send' AND \"AttemptCount\" > 0 AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL AND \"FailureType\" IS NULL AND \"HttpStatusCode\" IS NULL AND (\"RejectionCode\" IS NULL OR length(\"RejectionCode\") > 0)) OR (\"Status\" = 'Ambiguous' AND \"Message\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"FailurePhase\" = 'Send' AND \"AttemptCount\" > 0 AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL) OR (\"Status\" = 'Unexpected' AND \"Message\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"AttemptCount\" = 0 AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL AND \"FailurePhase\" = 'Preparation' AND length(\"FailureType\") > 0 AND \"RejectionCode\" IS NULL) OR (\"Status\" = 'Expired' AND \"Message\" IS NULL AND \"DeduplicationKey\" IS NULL AND \"NextAttemptAtUtc\" IS NULL AND \"ClaimToken\" IS NULL AND \"ClaimSlot\" IS NULL AND \"ClaimExpiresAtUtc\" IS NULL AND \"SendStartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"FailurePhase\" IS NULL AND \"FailureType\" IS NULL AND \"HttpStatusCode\" IS NULL AND \"RejectionCode\" IS NULL)"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_Status",
                        "\"Status\" IN ('Ambiguous', 'Claimed', 'Expired', 'MissingBot', 'MissingChannel', 'Pending', 'Rejected', 'SafePreSendExhausted', 'SafePreSendTransient', 'Sending', 'Unexpected')"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "public_chat_send_receipts",
                columns: table => new
                {
                    OutboxMessageId = table.Column<long>(type: "bigint", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    DeliveredDeduplicationKey = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    DeliveredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    TwitchMessageId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_chat_send_receipts", x => x.OutboxMessageId);
                    table.CheckConstraint(
                        "CK_public_chat_send_receipts_Delivery",
                        "(\"DeliveredDeduplicationKey\" IS NULL AND \"DeliveredAtUtc\" IS NULL) OR (length(\"DeliveredDeduplicationKey\") = 64 AND \"DeliveredAtUtc\" IS NOT NULL)"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "site_access_entries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_access_entries", x => x.Id);
                    table.CheckConstraint(
                        "CK_site_access_entries_Kind",
                        "\"Kind\" IN ('blacklist', 'whitelist')"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "site_access_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    WhitelistEnabled = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_access_settings", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_audits",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CollectiveId = table.Column<long>(type: "bigint", nullable: false),
                    OperationId = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Action = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    ActingHostId = table.Column<int>(type: "integer", nullable: false),
                    AffectedHostId = table.Column<int>(type: "integer", nullable: true),
                    ActorTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_audits_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_goals",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CollectiveId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    UnitName = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Target = table.Column<long>(type: "bigint", nullable: false),
                    Current = table.Column<long>(type: "bigint", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_goals_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_local_settings",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CollectiveId = table.Column<long>(type: "bigint", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Notification = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_local_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_local_settings_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_raid_relays",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CollectiveId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    CurrentHostId = table.Column<int>(type: "integer", nullable: false),
                    NextHostId = table.Column<int>(type: "integer", nullable: true),
                    AggregateViewerCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    LastSourceEventAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_raid_relays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_raid_relays_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_tournament_references",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CollectiveId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerHostId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionPublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Format = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    EntrantCount = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedResultCount = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    LastSourceEventAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_tournament_references", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_tournament_references_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "active_public_chat_pins",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    TwitchMessageId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PinnerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Feature = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ReplyKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    UnpinOnOwnerCompletion = table.Column<bool>(type: "boolean", nullable: false),
                    PinnedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_active_public_chat_pins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_active_public_chat_pins_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "approved_raid_channels",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ApprovedClipId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ApprovedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approved_raid_channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approved_raid_channels_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automatic_raid_processed_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ClaimedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_raid_processed_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_automatic_raid_processed_events_Expiry",
                        "\"ExpiresAtUtc\" >= \"ClaimedAtUtc\""
                    );
                    table.ForeignKey(
                        name: "FK_automatic_raid_processed_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automatic_raid_shoutout_outcomes",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SourceTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    SourceLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SourceDisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    ResultCode = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    MessageTimestampUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ClaimedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_raid_shoutout_outcomes", x => x.Id);
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_ResultCode",
                        "\"ResultCode\" IS NULL OR \"ResultCode\" IN ('Ambiguous', 'AuthorityRequired', 'Cooldown', 'Delivered', 'Invalid', 'NotReady', 'PartialFailure', 'Queued', 'RateLimited', 'Rejected', 'RuntimeMessageTooLong', 'Unexpected')"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_State",
                        "(\"Status\" = 'Processing' AND \"ResultCode\" IS NULL AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" = 'Queued' AND \"ResultCode\" = 'Queued' AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" = 'Delivered' AND \"ResultCode\" = 'Delivered' AND \"CompletedAtUtc\" IS NOT NULL) OR (\"Status\" = 'NotDelivered' AND \"ResultCode\" IS NOT NULL AND \"ResultCode\" NOT IN ('Queued', 'Delivered', 'Ambiguous') AND \"CompletedAtUtc\" IS NOT NULL) OR (\"Status\" = 'Ambiguous' AND \"ResultCode\" = 'Ambiguous' AND \"CompletedAtUtc\" IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_Status",
                        "\"Status\" IN ('Ambiguous', 'Delivered', 'NotDelivered', 'Processing', 'Queued')"
                    );
                    table.ForeignKey(
                        name: "FK_automatic_raid_shoutout_outcomes_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automatic_raid_shoutout_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    OnlyApprovedChannels = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    MinimumViewerCount = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 1
                    ),
                    Mechanism = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "Native"
                    ),
                    ChatPresentation = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "Regular"
                    ),
                    MessageTemplate = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: false,
                        defaultValue: "Welcome {display_name}! Check them out at {channel_url}"
                    ),
                    PinDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    AnnouncementColor = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "Primary"
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_raid_shoutout_settings", x => x.Id);
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_AnnouncementColor",
                        "\"AnnouncementColor\" IN ('Blue', 'Green', 'Orange', 'Primary', 'Purple')"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_ChatPresentation",
                        "\"ChatPresentation\" IN ('Announcement', 'Pinned', 'Regular')"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_Mechanism",
                        "\"Mechanism\" IN ('Chat', 'Native')"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_MinimumViewerCount",
                        "\"MinimumViewerCount\" >= 1"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_PinDuration",
                        "\"PinDurationSeconds\" IS NULL OR (\"PinDurationSeconds\" >= 30 AND \"PinDurationSeconds\" <= 1800)"
                    );
                    table.ForeignKey(
                        name: "FK_automatic_raid_shoutout_settings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_event_receipts",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SourceDefinitionId = table.Column<string>(
                        type: "character varying(96)",
                        maxLength: 96,
                        nullable: false
                    ),
                    ProviderMessageId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ClaimedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_event_receipts",
                        x => new
                        {
                            x.HostId,
                            x.SourceDefinitionId,
                            x.ProviderMessageId,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_automation_event_receipts_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_flows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UseVerticalLayout = table.Column<bool>(type: "boolean", nullable: false),
                    UseSmoothEdges = table.Column<bool>(type: "boolean", nullable: false),
                    UnavailableReason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_flows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_flows_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_event_receipts",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    SourceEventId = table.Column<string>(
                        type: "character varying(240)",
                        maxLength: 240,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    RecordedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_event_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_event_receipts_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    CardId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    OperationKey = table.Column<string>(
                        type: "character varying(240)",
                        maxLength: 240,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_moderation_audit",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    CardId = table.Column<long>(type: "bigint", nullable: true),
                    MarkId = table.Column<long>(type: "bigint", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    ActorTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateNote = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_moderation_audit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_moderation_audit_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_templates",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    CurrentRevision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_templates_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bloke_raid_campaigns",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    StartOperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    BossName = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    MaximumHealth = table.Column<int>(type: "integer", nullable: false),
                    CurrentHealth = table.Column<int>(type: "integer", nullable: false),
                    MaximumWard = table.Column<int>(type: "integer", nullable: false),
                    CurrentWard = table.Column<int>(type: "integer", nullable: false),
                    CurrentPhase = table.Column<int>(type: "integer", nullable: false),
                    VictoryPointReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ResetPolicy = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EndsAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    VictoryRewardedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_campaigns", x => x.Id);
                    table.UniqueConstraint(
                        "AK_bloke_raid_campaigns_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_bloke_raid_campaigns_ResetPolicy",
                        "\"ResetPolicy\" IN ('Manual', 'Weekly')"
                    );
                    table.CheckConstraint(
                        "CK_bloke_raid_campaigns_Status",
                        "\"Status\" IN ('Active', 'Ended', 'Expired', 'Victory')"
                    );
                    table.ForeignKey(
                        name: "FK_bloke_raid_campaigns_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bloke_raid_configurations",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    BossName = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    MaximumHealth = table.Column<int>(type: "integer", nullable: false),
                    MaximumWard = table.Column<int>(type: "integer", nullable: false),
                    CampaignDurationHours = table.Column<int>(type: "integer", nullable: false),
                    AttackMinimum = table.Column<int>(type: "integer", nullable: false),
                    AttackMaximum = table.Column<int>(type: "integer", nullable: false),
                    AttackCooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    AttackPerStreamLimit = table.Column<int>(type: "integer", nullable: false),
                    MendMinimum = table.Column<int>(type: "integer", nullable: false),
                    MendMaximum = table.Column<int>(type: "integer", nullable: false),
                    MendCooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    MendPerStreamLimit = table.Column<int>(type: "integer", nullable: false),
                    SpecialMinimum = table.Column<int>(type: "integer", nullable: false),
                    SpecialMaximum = table.Column<int>(type: "integer", nullable: false),
                    SpecialCooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    SpecialPerStreamLimit = table.Column<int>(type: "integer", nullable: false),
                    SpecialPointCost = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CorrectGuessDamage = table.Column<int>(type: "integer", nullable: false),
                    VictoryPointReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PhaseTwoHealthPercent = table.Column<int>(type: "integer", nullable: false),
                    PhaseThreeHealthPercent = table.Column<int>(type: "integer", nullable: false),
                    PhaseOneResponse = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    PhaseTwoResponse = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    PhaseThreeResponse = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    VictoryResponse = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    ExpiryResponse = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    ResetPolicy = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    WeeklyResetDay = table.Column<int>(type: "integer", nullable: false),
                    WeeklyResetHourUtc = table.Column<int>(type: "integer", nullable: false),
                    NextWeeklyResetAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_configurations", x => x.Id);
                    table.CheckConstraint(
                        "CK_bloke_raid_configurations_ResetPolicy",
                        "\"ResetPolicy\" IN ('Manual', 'Weekly')"
                    );
                    table.ForeignKey(
                        name: "FK_bloke_raid_configurations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bounties",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationFingerprint = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Visibility = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    FailurePledgePolicy = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    RewardDistribution = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    FundingTarget = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PledgedAmount = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ContributorCount = table.Column<int>(type: "integer", nullable: false),
                    CompletionReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    AcceptedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ResolvedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bounties", x => x.Id);
                    table.UniqueConstraint("AK_bounties_HostId_Id", x => new { x.HostId, x.Id });
                    table.CheckConstraint(
                        "CK_bounties_ContributorCount",
                        "\"ContributorCount\" >= 0"
                    );
                    table.CheckConstraint(
                        "CK_bounties_FailurePledgePolicy",
                        "\"FailurePledgePolicy\" IN ('Refund', 'Spend')"
                    );
                    table.CheckConstraint("CK_bounties_Revision", "\"Revision\" > 0");
                    table.CheckConstraint(
                        "CK_bounties_RewardDistribution",
                        "\"RewardDistribution\" IN ('Equal', 'Proportional')"
                    );
                    table.CheckConstraint(
                        "CK_bounties_Status",
                        "\"Status\" IN ('Accepted', 'Cancelled', 'Completed', 'Expired', 'Failed', 'Funding', 'Proposed')"
                    );
                    table.CheckConstraint(
                        "CK_bounties_Visibility",
                        "\"Visibility\" IN ('Private', 'Public')"
                    );
                    table.ForeignKey(
                        name: "FK_bounties_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_memberships",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CollectiveId = table.Column<long>(type: "bigint", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    AcceptWorkAfterUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    InvitedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    RespondedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_memberships_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_collective_memberships_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_seasons",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    ModeratorNotes = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Visibility = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    StartsAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EndsAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    OpenedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ClosedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ArchivedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_seasons_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_source_event_receipts",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SourceKind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    SourceEventId = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    ProcessedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_source_event_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_source_event_receipts_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competitions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    Format = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    EntryKind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Seeding = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Tiebreak = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    TeamSize = table.Column<int>(type: "integer", nullable: false),
                    MinimumPoints = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    WinPoints = table.Column<int>(type: "integer", nullable: false),
                    DrawPoints = table.Column<int>(type: "integer", nullable: false),
                    LossPoints = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    AlgorithmVersion = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ReminderHoursBefore = table.Column<int>(type: "integer", nullable: false),
                    ReminderMessage = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    WinnerPoints = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    RunnerUpPoints = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    WinnerAchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    RunnerUpAchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    PrivateLobbyInformation = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    RegistrationOpenedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ArchivedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competitions", x => x.Id);
                    table.UniqueConstraint(
                        "AK_competitions_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_competitions_Capacity",
                        "\"Capacity\" BETWEEN 2 AND 128"
                    );
                    table.CheckConstraint("CK_competitions_DrawPoints", "\"DrawPoints\" >= 0");
                    table.CheckConstraint("CK_competitions_LossPoints", "\"LossPoints\" >= 0");
                    table.CheckConstraint("CK_competitions_Revision", "\"Revision\" > 0");
                    table.CheckConstraint(
                        "CK_competitions_TeamSize",
                        "\"TeamSize\" BETWEEN 1 AND 32"
                    );
                    table.CheckConstraint("CK_competitions_WinPoints", "\"WinPoints\" >= 0");
                    table.ForeignKey(
                        name: "FK_competitions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "configuration_activations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    EnabledChanges = table.Column<long>(type: "bigint", nullable: false),
                    DisabledChanges = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    IssuesJson = table.Column<string>(
                        type: "character varying(4096)",
                        maxLength: 4096,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_activations", x => x.Id);
                    table.CheckConstraint(
                        "CK_configuration_activations_Status",
                        "\"Status\" IN ('Complete', 'Failed', 'ManualFollowUp', 'Pending', 'Processing')"
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
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    OperationId = table.Column<string>(
                        type: "character varying(36)",
                        nullable: false
                    ),
                    ActorTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SourceFormatVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    SummaryJson = table.Column<string>(
                        type: "character varying(2048)",
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

            migrationBuilder.CreateTable(
                name: "custom_announcement_delivery_policies",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    PolicyType = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    RetryDelayTicks = table.Column<long>(type: "bigint", nullable: true),
                    OccurrenceLifetimeTicks = table.Column<long>(type: "bigint", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_announcement_delivery_policies", x => x.Id);
                    table.UniqueConstraint(
                        "AK_custom_announcement_delivery_policies_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_custom_announcement_delivery_policies_Payload",
                        "\"PolicyType\" = 'RetryUntilExpiredThenSkip' AND \"RetryDelayTicks\" IS NOT NULL AND \"RetryDelayTicks\" > 0 AND \"OccurrenceLifetimeTicks\" IS NOT NULL AND \"OccurrenceLifetimeTicks\" <= 600000000 AND \"RetryDelayTicks\" < \"OccurrenceLifetimeTicks\""
                    );
                    table.CheckConstraint(
                        "CK_custom_announcement_delivery_policies_PolicyType",
                        "\"PolicyType\" IN ('RetryUntilExpiredThenSkip')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_announcement_delivery_policies_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_commands",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowEveryone = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true
                    ),
                    AllowModerators = table.Column<bool>(type: "boolean", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    CooldownScope = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    InvocationLimit = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false,
                        defaultValue: "Unlimited"
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_commands", x => x.Id);
                    table.UniqueConstraint(
                        "AK_custom_commands_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_custom_commands_CooldownScope",
                        "\"CooldownScope\" IN ('Global', 'User')"
                    );
                    table.CheckConstraint(
                        "CK_custom_commands_InvocationLimit",
                        "\"InvocationLimit\" IN ('OncePerStream', 'OncePerStreamPerUser', 'OncePerUser', 'Unlimited')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_commands_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_counters",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_counters", x => x.Id);
                    table.UniqueConstraint(
                        "AK_custom_counters_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_custom_counters_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_message_library_entries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SelectionMode = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    CurrentVariantIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_message_library_entries", x => x.Id);
                    table.UniqueConstraint(
                        "AK_custom_message_library_entries_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_custom_message_library_entries_SelectionMode",
                        "\"SelectionMode\" IN ('First', 'Random', 'Sequential')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_message_library_entries_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "durable_alerts",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Source = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    SourceKey = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Message = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    LinkPath = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    OccurrenceCount = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 1
                    ),
                    LastOccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    AcknowledgedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    AcknowledgedByLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_durable_alerts", x => x.Id);
                    table.CheckConstraint(
                        "CK_durable_alerts_Severity",
                        "\"Severity\" IN ('Critical', 'Info', 'Warning')"
                    );
                    table.ForeignKey(
                        name: "FK_durable_alerts_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "guess_round_profiles",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Slug = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(
                        type: "bigint",
                        nullable: false,
                        defaultValue: 0L
                    ),
                    WinningGuessPointReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false,
                        defaultValue: "0"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guess_round_profiles", x => x.Id);
                    table.UniqueConstraint(
                        "AK_guess_round_profiles_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_guess_round_profiles_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "host_bot_account_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    OverrideEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WhisperResponsesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ProfileImageUrl = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    ProtectedTokenPayload = table.Column<byte[]>(type: "bytea", nullable: true),
                    AuthorizedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    AuthorizedScopes = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_bot_account_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_host_bot_account_settings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "host_broadcaster_authorizations",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ProtectedTokenPayload = table.Column<byte[]>(type: "bytea", nullable: true),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    AuthorizedScopes = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    AuthorizedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_broadcaster_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_host_broadcaster_authorizations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "host_mod_access_entries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_mod_access_entries", x => x.Id);
                    table.CheckConstraint(
                        "CK_host_mod_access_entries_Kind",
                        "\"Kind\" IN ('blacklist', 'whitelist')"
                    );
                    table.ForeignKey(
                        name: "FK_host_mod_access_entries_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "host_mod_access_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ModsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowModsByDefault = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_mod_access_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_host_mod_access_settings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_hub_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    MergeWindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    MarkerFallbackEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RewardPolicy = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    RewardAmount = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_hub_settings", x => x.Id);
                    table.CheckConstraint(
                        "CK_moment_hub_settings_MergeWindowSeconds",
                        "\"MergeWindowSeconds\" BETWEEN 15 AND 300"
                    );
                    table.CheckConstraint(
                        "CK_moment_hub_settings_RewardPolicy",
                        "\"RewardPolicy\" IN ('AllContributors', 'FirstRequester', 'None')"
                    );
                    table.ForeignKey(
                        name: "FK_moment_hub_settings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_cues",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DurationMilliseconds = table.Column<int>(type: "integer", nullable: false),
                    QueuePolicy = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ConfigurationJson = table.Column<string>(
                        type: "character varying(32768)",
                        maxLength: 32768,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_cues", x => x.Id);
                    table.UniqueConstraint(
                        "AK_overlay_cues_Id_HostId",
                        x => new { x.Id, x.HostId }
                    );
                    table.CheckConstraint(
                        "CK_overlay_cues_ConfigurationJson",
                        "length(\"ConfigurationJson\") BETWEEN 1 AND 32768 AND jsonb_typeof(\"ConfigurationJson\"::jsonb) = 'object' AND jsonb_typeof((\"ConfigurationJson\"::jsonb)->'schemaVersion') = 'number' AND (\"ConfigurationJson\"::jsonb)->>'schemaVersion' = '1'"
                    );
                    table.CheckConstraint(
                        "CK_overlay_cues_Duration",
                        "\"DurationMilliseconds\" BETWEEN 100 AND 300000"
                    );
                    table.CheckConstraint(
                        "CK_overlay_cues_Name",
                        "length(\"Name\") BETWEEN 1 AND 128"
                    );
                    table.CheckConstraint(
                        "CK_overlay_cues_QueuePolicy",
                        "\"QueuePolicy\" IN ('concurrent', 'enqueue', 'ignore', 'replace')"
                    );
                    table.CheckConstraint("CK_overlay_cues_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_overlay_cues_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_instance_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    OverlayPublicId = table.Column<string>(
                        type: "character varying(36)",
                        nullable: false
                    ),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ActorUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    OverlayRevision = table.Column<long>(type: "bigint", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_instance_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_overlay_instance_events_Kind",
                        "\"Kind\" IN ('configured', 'created', 'deleted', 'disabled', 'enabled', 'key-rotated', 'renamed')"
                    );
                    table.ForeignKey(
                        name: "FK_overlay_instance_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_instances",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Type = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigurationJson = table.Column<string>(
                        type: "character varying(8192)",
                        maxLength: 8192,
                        nullable: false
                    ),
                    AccessKeyDigest = table.Column<byte[]>(
                        type: "bytea",
                        maxLength: 32,
                        nullable: false
                    ),
                    RequiresAccessKeyRegeneration = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_instances", x => x.Id);
                    table.CheckConstraint(
                        "CK_overlay_instances_AccessKeyDigest",
                        "length(\"AccessKeyDigest\") = 32"
                    );
                    table.CheckConstraint(
                        "CK_overlay_instances_ConfigurationJson",
                        "length(\"ConfigurationJson\") BETWEEN 1 AND 8192 AND jsonb_typeof(\"ConfigurationJson\"::jsonb) = 'object' AND jsonb_typeof((\"ConfigurationJson\"::jsonb)->'schemaVersion') = 'number' AND (\"ConfigurationJson\"::jsonb)->>'schemaVersion' = '1'"
                    );
                    table.CheckConstraint(
                        "CK_overlay_instances_Name",
                        "length(\"Name\") BETWEEN 1 AND 128"
                    );
                    table.CheckConstraint(
                        "CK_overlay_instances_Type",
                        "\"Type\" IN ('community-goal', 'cue-player', 'empty', 'event-feed', 'giveaway', 'guessing', 'viewer-funded-bounty', 'viewer-queue')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_instances_Versions",
                        "\"KeyVersion\" > 0 AND \"Revision\" > 0"
                    );
                    table.ForeignKey(
                        name: "FK_overlay_instances_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "play_queues",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Slug = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Name = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    ActivityName = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    SelectionMode = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ShowParticipantNames = table.Column<bool>(type: "boolean", nullable: false),
                    ReadinessTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    HistoryRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    SkipExclusionMinutes = table.Column<int>(type: "integer", nullable: false),
                    CurrentPartyNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queues", x => x.Id);
                    table.CheckConstraint(
                        "CK_play_queues_SelectionMode",
                        "\"SelectionMode\" IN ('JoinOrder', 'LeastRecentParticipation')"
                    );
                    table.ForeignKey(
                        name: "FK_play_queues_hosts_HostId",
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
                    PluginId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    FeatureId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ValuesJson = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
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
                        "jsonb_typeof(\"ValuesJson\"::jsonb) = 'array' AND octet_length(\"ValuesJson\") <= 65536"
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
                    PluginId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    FeatureId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    LifecycleOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerGeneration = table.Column<long>(type: "bigint", nullable: false),
                    FeatureGeneration = table.Column<long>(type: "bigint", nullable: false),
                    Readiness = table.Column<string>(type: "text", nullable: false),
                    ReasonCode = table.Column<string>(type: "text", nullable: true),
                    RecoveryAction = table.Column<string>(type: "text", nullable: true),
                    ReasonDetail = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
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
                name: "point_balances",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Amount = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_balances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_point_balances_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "points_giveaways",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EndsAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    MinimumPayout = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    MaximumPayout = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    WinnerCount = table.Column<int>(type: "integer", nullable: false),
                    Eligibility = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_giveaways", x => x.Id);
                    table.CheckConstraint(
                        "CK_points_giveaways_Eligibility",
                        "\"Eligibility\" IN ('everyone', 'followers', 'subscribers')"
                    );
                    table.CheckConstraint(
                        "CK_points_giveaways_Status",
                        "\"Status\" IN ('Active', 'Cancelled', 'Completed', 'Expired')"
                    );
                    table.ForeignKey(
                        name: "FK_points_giveaways_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "points_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    PointLabel = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    GamblingWinRatePercent = table.Column<int>(type: "integer", nullable: false),
                    GamblingCooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    GiveawayDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    GiveawayMinimumPayout = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    GiveawayMaximumPayout = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    GiveawayWinnerCount = table.Column<int>(type: "integer", nullable: false),
                    GiveawayEligibility = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    GiveawayCooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    BalanceReply = table.Column<string>(type: "text", nullable: false),
                    OtherBalanceReply = table.Column<string>(type: "text", nullable: false),
                    TransferReply = table.Column<string>(type: "text", nullable: false),
                    AddReply = table.Column<string>(type: "text", nullable: false),
                    RemoveReply = table.Column<string>(type: "text", nullable: false),
                    InvalidAmountReply = table.Column<string>(type: "text", nullable: false),
                    InsufficientBalanceReply = table.Column<string>(type: "text", nullable: false),
                    ModeratorOnlyReply = table.Column<string>(type: "text", nullable: false),
                    GamblingWinReply = table.Column<string>(type: "text", nullable: false),
                    GamblingLoseReply = table.Column<string>(type: "text", nullable: false),
                    GiveawayStartedReply = table.Column<string>(type: "text", nullable: false),
                    GiveawayUpdateReply = table.Column<string>(type: "text", nullable: false),
                    GiveawayJoinedReply = table.Column<string>(type: "text", nullable: false),
                    GiveawayAlreadyJoinedReply = table.Column<string>(
                        type: "text",
                        nullable: false
                    ),
                    GiveawayEndedReply = table.Column<string>(type: "text", nullable: false),
                    GiveawayNoEntrantsReply = table.Column<string>(type: "text", nullable: false),
                    GiveawayCancelledReply = table.Column<string>(type: "text", nullable: false),
                    GiveawayAlreadyActiveReply = table.Column<string>(
                        type: "text",
                        nullable: false
                    ),
                    GiveawayNotActiveReply = table.Column<string>(type: "text", nullable: false),
                    GiveawayCooldownReply = table.Column<string>(type: "text", nullable: false),
                    StreamOfflineReply = table.Column<string>(type: "text", nullable: false),
                    NotEligibleReply = table.Column<string>(type: "text", nullable: false),
                    FollowerChecksUnavailableReply = table.Column<string>(
                        type: "text",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_settings", x => x.Id);
                    table.CheckConstraint(
                        "CK_points_settings_GiveawayEligibility",
                        "\"GiveawayEligibility\" IN ('everyone', 'followers', 'subscribers')"
                    );
                    table.ForeignKey(
                        name: "FK_points_settings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "raid_collaboration_history",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Direction = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    OtherTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    OtherLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    OtherDisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerCount = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ProviderStreamId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    WelcomeOutcome = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    ShoutoutOutcome = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    RecordedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raid_collaboration_history", x => x.Id);
                    table.CheckConstraint(
                        "CK_raid_collaboration_history_Direction",
                        "\"Direction\" IN ('Incoming', 'Outgoing')"
                    );
                    table.CheckConstraint(
                        "CK_raid_collaboration_history_ShoutoutOutcome",
                        "\"ShoutoutOutcome\" IN ('Cooldown', 'Deduplicated', 'NotConfigured', 'NotEligible', 'Queued', 'Rejected', 'Sent', 'Suppressed')"
                    );
                    table.CheckConstraint(
                        "CK_raid_collaboration_history_WelcomeOutcome",
                        "\"WelcomeOutcome\" IN ('Deduplicated', 'Delivered', 'NotConfigured', 'Rejected', 'Suppressed')"
                    );
                    table.ForeignKey(
                        name: "FK_raid_collaboration_history_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "raid_collaboration_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    WelcomeEnabled = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true
                    ),
                    WelcomeMessage = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false,
                        defaultValue: "Welcome {display_name} and community!"
                    ),
                    DeduplicationWindowMinutes = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 60
                    ),
                    Language = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "en"
                    ),
                    EligibleCategories = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    RelationshipCooldownHours = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 336
                    ),
                    IncludeFollowedLiveChannels = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raid_collaboration_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_raid_collaboration_settings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "reply_delivery_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Feature = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ScopeId = table.Column<int>(type: "integer", nullable: false),
                    ReplyKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Target = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reply_delivery_settings", x => x.Id);
                    table.CheckConstraint(
                        "CK_reply_delivery_settings_Feature",
                        "\"Feature\" IN ('guessing', 'points')"
                    );
                    table.CheckConstraint(
                        "CK_reply_delivery_settings_Target",
                        "\"Target\" IN ('chat', 'whisper')"
                    );
                    table.ForeignKey(
                        name: "FK_reply_delivery_settings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "reply_pin_policies",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Feature = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ReplyKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    UnpinOnOwnerCompletion = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reply_pin_policies", x => x.Id);
                    table.CheckConstraint(
                        "CK_reply_pin_policies_DurationSeconds",
                        "\"DurationSeconds\" IS NULL OR \"DurationSeconds\" BETWEEN 30 AND 1800"
                    );
                    table.ForeignKey(
                        name: "FK_reply_pin_policies_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_boards",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Slug = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    PointCost = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    RefundPolicy = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    SubmissionLimitPerUser = table.Column<int>(type: "integer", nullable: false),
                    SubmissionCooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    VoteLimitPerUser = table.Column<int>(type: "integer", nullable: false),
                    VotingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    OrderingDescription = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_boards", x => x.Id);
                    table.CheckConstraint(
                        "CK_request_boards_RefundPolicy",
                        "\"RefundPolicy\" IN ('AnyUnfulfilledClosure', 'Never', 'RejectedOrWithdrawn')"
                    );
                    table.ForeignKey(
                        name: "FK_request_boards_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "shoutout_cooldowns",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GlobalEligibleAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    TargetTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    TargetLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    TargetEligibleAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoutout_cooldowns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shoutout_cooldowns_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "shoutout_history",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    SourceTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    SourceLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    TargetTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    TargetLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerCount = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CooldownEndsAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    TargetCooldownEndsAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoutout_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shoutout_history_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_clips",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ProviderClipId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    EditUrl = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: true
                    ),
                    FinalUrl = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: true
                    ),
                    BroadcasterTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    BroadcasterLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    CreatorTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    CreatorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    VideoId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    FailureReason = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    RequestedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ResolvedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastCheckedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_clips", x => x.Id);
                    table.CheckConstraint(
                        "CK_twitch_clips_Status",
                        "\"Status\" IN ('Ambiguous', 'Available', 'Expired', 'Failed', 'Pending')"
                    );
                    table.ForeignKey(
                        name: "FK_twitch_clips_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_custom_rewards",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ProviderRewardId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(45)",
                        maxLength: 45,
                        nullable: false
                    ),
                    Prompt = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    Cost = table.Column<int>(type: "integer", nullable: false),
                    IsManageable = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    IsUserInputRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsMaxPerStreamEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaxPerStream = table.Column<int>(type: "integer", nullable: true),
                    IsMaxPerUserPerStreamEnabled = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    MaxPerUserPerStream = table.Column<int>(type: "integer", nullable: true),
                    IsGlobalCooldownEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GlobalCooldownSeconds = table.Column<int>(type: "integer", nullable: true),
                    ShouldRedemptionsSkipRequestQueue = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    BackgroundColor = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_custom_rewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_custom_rewards_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_poll_templates",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(
                        type: "character varying(60)",
                        maxLength: 60,
                        nullable: false
                    ),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ChannelPointsVotingEnabled = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    ChannelPointsPerVote = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_poll_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_poll_templates_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_polls",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ProviderPollId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(60)",
                        maxLength: 60,
                        nullable: false
                    ),
                    ChoicesJson = table.Column<string>(
                        type: "character varying(4096)",
                        maxLength: 4096,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    IsExternallyStarted = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EndsAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    EndedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_polls", x => x.Id);
                    table.CheckConstraint(
                        "CK_twitch_polls_Status",
                        "\"Status\" IN ('Active', 'Archived', 'Completed', 'Terminated')"
                    );
                    table.ForeignKey(
                        name: "FK_twitch_polls_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_prediction_templates",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(
                        type: "character varying(45)",
                        maxLength: 45,
                        nullable: false
                    ),
                    PredictionWindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_prediction_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_prediction_templates_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_predictions",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ProviderPredictionId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(45)",
                        maxLength: 45,
                        nullable: false
                    ),
                    OutcomesJson = table.Column<string>(
                        type: "character varying(16384)",
                        maxLength: 16384,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    IsExternallyStarted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LocksAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    EndedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_predictions", x => x.Id);
                    table.CheckConstraint(
                        "CK_twitch_predictions_Status",
                        "\"Status\" IN ('Active', 'Archived', 'Canceled', 'Locked', 'Resolved')"
                    );
                    table.ForeignKey(
                        name: "FK_twitch_predictions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_reward_redemptions",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ProviderRedemptionId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ProviderRewardId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    RewardTitle = table.Column<string>(
                        type: "character varying(45)",
                        maxLength: 45,
                        nullable: false
                    ),
                    UserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    UserLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    UserInput = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    RedeemedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_reward_redemptions", x => x.Id);
                    table.CheckConstraint(
                        "CK_twitch_reward_redemptions_Status",
                        "\"Status\" IN ('Canceled', 'Fulfilled', 'Unfulfilled')"
                    );
                    table.ForeignKey(
                        name: "FK_twitch_reward_redemptions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_stream_markers",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ProviderMarkerId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    Description = table.Column<string>(
                        type: "character varying(140)",
                        maxLength: 140,
                        nullable: false
                    ),
                    PositionSeconds = table.Column<int>(type: "integer", nullable: false),
                    MarkerUrl = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: true
                    ),
                    VideoId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    FailureReason = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ResolvedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    EnrichedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_stream_markers", x => x.Id);
                    table.CheckConstraint(
                        "CK_twitch_stream_markers_Status",
                        "\"Status\" IN ('Ambiguous', 'Failed', 'Succeeded')"
                    );
                    table.ForeignKey(
                        name: "FK_twitch_stream_markers_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "viewer_passport_ambiguous_logins",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DetectedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_ambiguous_logins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passport_ambiguous_logins_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "viewer_passport_stream_sessions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    TwitchStreamId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ContinuityGeneration = table.Column<int>(type: "integer", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_stream_sessions", x => x.Id);
                    table.UniqueConstraint(
                        "AK_viewer_passport_stream_sessions_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passport_stream_sessions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "whisper_quota_buckets",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    BotTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    DayUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Exhausted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whisper_quota_buckets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whisper_quota_buckets_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_media_assets",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ContentRevision = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<string>(
                        type: "character varying(36)",
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_media_assets", x => x.Id);
                    table.UniqueConstraint(
                        "AK_overlay_media_assets_Id_HostId",
                        x => new { x.Id, x.HostId }
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_assets_Length",
                        "\"ContentRevision\" > 0"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_assets_Name",
                        "length(\"Name\") BETWEEN 1 AND 128"
                    );
                    table.ForeignKey(
                        name: "FK_overlay_media_assets_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_overlay_media_assets_overlay_media_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "overlay_media_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_installation_secrets",
                columns: table => new
                {
                    PluginId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    SettingId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ProtectedValue = table.Column<byte[]>(type: "bytea", nullable: false),
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
                        name: "FK_plugin_installation_secrets_plugin_installation_configurati~",
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
                    PluginId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    DeclaredVersion = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SnapshotId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false
                    ),
                    Summary = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    Author = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    IconUrl = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: true
                    ),
                    RepositoryUrl = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: false
                    ),
                    PackagePath = table.Column<string>(
                        type: "character varying(240)",
                        maxLength: 240,
                        nullable: false
                    ),
                    CompatibilityBlokeBot = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    CompatibilityPluginApi = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    CompatibilityLua = table.Column<string>(
                        type: "character varying(8)",
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
                        name: "FK_plugin_marketplace_catalog_entries_plugin_marketplace_catal~",
                        column: x => x.SnapshotId,
                        principalTable: "plugin_marketplace_catalog_state",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "public_chat_pin_operations",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    Kind = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    OutboxMessageId = table.Column<long>(type: "bigint", nullable: true),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Feature = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ReplyKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    UnpinOnOwnerCompletion = table.Column<bool>(type: "boolean", nullable: false),
                    TwitchMessageId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PinnerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    AttemptStartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Outcome = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_chat_pin_operations", x => x.Id);
                    table.CheckConstraint(
                        "CK_public_chat_pin_operations_DurationSeconds",
                        "\"DurationSeconds\" IS NULL OR \"DurationSeconds\" BETWEEN 30 AND 1800"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_pin_operations_Kind",
                        "\"Kind\" IN ('Pin', 'Unpin')"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_pin_operations_Status",
                        "\"Status\" IN ('Attempting', 'AwaitingDelivery', 'NoOp', 'Ready', 'Succeeded', 'Terminal')"
                    );
                    table.ForeignKey(
                        name: "FK_public_chat_pin_operations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_public_chat_pin_operations_public_chat_outbox_OutboxMessage~",
                        column: x => x.OutboxMessageId,
                        principalTable: "public_chat_outbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_goal_host_totals",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CollectiveGoalId = table.Column<long>(type: "bigint", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SourceBountyPublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Total = table.Column<long>(type: "bigint", nullable: false),
                    LastSourceEventAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_goal_host_totals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_goal_host_totals_collective_goals_CollectiveGoal~",
                        column: x => x.CollectiveGoalId,
                        principalTable: "collective_goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_raid_handoffs",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CollectiveRaidRelayId = table.Column<long>(type: "bigint", nullable: false),
                    OperationId = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    FromHostId = table.Column<int>(type: "integer", nullable: false),
                    ToHostId = table.Column<int>(type: "integer", nullable: false),
                    AggregateViewerCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_raid_handoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_raid_handoffs_collective_raid_relays_CollectiveR~",
                        column: x => x.CollectiveRaidRelayId,
                        principalTable: "collective_raid_relays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_flow_edges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    SourceNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePortId = table.Column<string>(
                        type: "character varying(96)",
                        maxLength: 96,
                        nullable: false
                    ),
                    TargetNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetPortId = table.Column<string>(
                        type: "character varying(96)",
                        maxLength: 96,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_flow_edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_flow_edges_automation_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "automation_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_flow_nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<string>(
                        type: "character varying(96)",
                        maxLength: 96,
                        nullable: false
                    ),
                    DefinitionSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    InputBindingsJson = table.Column<string>(type: "text", nullable: false),
                    ExpressionLanguageVersion = table.Column<int>(type: "integer", nullable: false),
                    ContinueOnFailure = table.Column<bool>(type: "boolean", nullable: false),
                    CanvasX = table.Column<int>(type: "integer", nullable: false),
                    CanvasY = table.Column<int>(type: "integer", nullable: false),
                    DisplayAlias = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    PluginProvenanceJson = table.Column<string>(type: "text", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_flow_nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_flow_nodes_automation_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "automation_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "automation_flow_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    AutomationGeneration = table.Column<int>(type: "integer", nullable: false),
                    RequiredFeatures = table.Column<long>(type: "bigint", nullable: false),
                    ContextSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    SourceDefinitionId = table.Column<string>(
                        type: "character varying(96)",
                        maxLength: 96,
                        nullable: false
                    ),
                    SourceNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceOccurrenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextJson = table.Column<string>(type: "text", nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ExecutionLeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_flow_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_flow_runs_automation_flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "automation_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_automation_instantiations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnableOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    FeatureId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    PluginVersion = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ManifestVersion = table.Column<int>(type: "integer", nullable: false),
                    TemplateHash = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: true),
                    Diagnostic = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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
                name: "bingo_template_revisions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    FullCardWinEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LinePointsReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    LineAchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: true
                    ),
                    FullCardPointsReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    FullCardAchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: true
                    ),
                    CreatedByTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedByLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_template_revisions", x => x.Id);
                    table.CheckConstraint(
                        "CK_bingo_template_revisions_Dimension",
                        "\"Dimension\" IN (3, 4, 5)"
                    );
                    table.CheckConstraint(
                        "CK_bingo_template_revisions_Revision",
                        "\"Revision\" > 0"
                    );
                    table.ForeignKey(
                        name: "FK_bingo_template_revisions_bingo_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "bingo_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bloke_raid_actions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CampaignId = table.Column<long>(type: "bigint", nullable: false),
                    OperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Source = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    StreamKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    PointCost = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    BossHealthBefore = table.Column<int>(type: "integer", nullable: false),
                    BossHealthAfter = table.Column<int>(type: "integer", nullable: false),
                    WardBefore = table.Column<int>(type: "integer", nullable: false),
                    WardAfter = table.Column<int>(type: "integer", nullable: false),
                    PhaseAfter = table.Column<int>(type: "integer", nullable: false),
                    GuessRoundId = table.Column<int>(type: "integer", nullable: true),
                    Response = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_actions", x => x.Id);
                    table.CheckConstraint(
                        "CK_bloke_raid_actions_Kind",
                        "\"Kind\" IN ('Attack', 'CorrectGuess', 'Mend', 'Special')"
                    );
                    table.CheckConstraint(
                        "CK_bloke_raid_actions_Source",
                        "\"Source\" IN ('Chat', 'Guessing')"
                    );
                    table.ForeignKey(
                        name: "FK_bloke_raid_actions_bloke_raid_campaigns_HostId_CampaignId",
                        columns: x => new { x.HostId, x.CampaignId },
                        principalTable: "bloke_raid_campaigns",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bloke_raid_contributions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CampaignId = table.Column<long>(type: "bigint", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Damage = table.Column<int>(type: "integer", nullable: false),
                    WardRestored = table.Column<int>(type: "integer", nullable: false),
                    ActionCount = table.Column<int>(type: "integer", nullable: false),
                    SpecialCount = table.Column<int>(type: "integer", nullable: false),
                    CorrectGuessCount = table.Column<int>(type: "integer", nullable: false),
                    LastContributedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_contributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bloke_raid_contributions_bloke_raid_campaigns_HostId_Campai~",
                        columns: x => new { x.HostId, x.CampaignId },
                        principalTable: "bloke_raid_campaigns",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bloke_raid_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CampaignId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    OperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "character varying(4096)",
                        maxLength: 4096,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_bloke_raid_events_Kind",
                        "\"Kind\" IN ('ActionResolved', 'CampaignEnded', 'CampaignExpired', 'CampaignReset', 'CampaignStarted', 'CampaignVictorious', 'PhaseChanged')"
                    );
                    table.ForeignKey(
                        name: "FK_bloke_raid_events_bloke_raid_campaigns_HostId_CampaignId",
                        columns: x => new { x.HostId, x.CampaignId },
                        principalTable: "bloke_raid_campaigns",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bounty_contributor_rewards",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    BountyId = table.Column<long>(type: "bigint", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Amount = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bounty_contributor_rewards", x => x.Id);
                    table.UniqueConstraint(
                        "AK_bounty_contributor_rewards_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_bounty_contributor_rewards_bounties_HostId_BountyId",
                        columns: x => new { x.HostId, x.BountyId },
                        principalTable: "bounties",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bounty_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    BountyId = table.Column<long>(type: "bigint", nullable: false),
                    BountyPublicId = table.Column<string>(
                        type: "character varying(36)",
                        nullable: false
                    ),
                    OperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bounty_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_bounty_events_Kind",
                        "\"Kind\" IN ('Accepted', 'Cancelled', 'Completed', 'Created', 'Expired', 'Extended', 'Failed', 'FundingOpened', 'FundingTargetReached', 'Pledged', 'PledgesConsumed', 'PledgesRefunded', 'RewardsDistributed')"
                    );
                    table.ForeignKey(
                        name: "FK_bounty_events_bounties_HostId_BountyId",
                        columns: x => new { x.HostId, x.BountyId },
                        principalTable: "bounties",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bounty_moderation_audit",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    BountyId = table.Column<long>(type: "bigint", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandFingerprint = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Action = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    FromStatus = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ToStatus = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ActorTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Reason = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    BountyRevision = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bounty_moderation_audit", x => x.Id);
                    table.CheckConstraint(
                        "CK_bounty_moderation_audit_Action",
                        "\"Action\" IN ('Accepted', 'Cancelled', 'Completed', 'Created', 'Expired', 'Extended', 'Failed', 'FundingOpened', 'PauseAdjusted', 'Rejected')"
                    );
                    table.CheckConstraint(
                        "CK_bounty_moderation_audit_FromStatus",
                        "\"FromStatus\" IN ('Accepted', 'Cancelled', 'Completed', 'Expired', 'Failed', 'Funding', 'Proposed')"
                    );
                    table.CheckConstraint(
                        "CK_bounty_moderation_audit_ToStatus",
                        "\"ToStatus\" IN ('Accepted', 'Cancelled', 'Completed', 'Expired', 'Failed', 'Funding', 'Proposed')"
                    );
                    table.ForeignKey(
                        name: "FK_bounty_moderation_audit_bounties_HostId_BountyId",
                        columns: x => new { x.HostId, x.BountyId },
                        principalTable: "bounties",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bounty_pledges",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    BountyId = table.Column<long>(type: "bigint", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandFingerprint = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ContributorTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ContributorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Amount = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    State = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bounty_pledges", x => x.Id);
                    table.UniqueConstraint(
                        "AK_bounty_pledges_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_bounty_pledges_State",
                        "\"State\" IN ('Consumed', 'Refunded', 'Reserved')"
                    );
                    table.ForeignKey(
                        name: "FK_bounty_pledges_bounties_HostId_BountyId",
                        columns: x => new { x.HostId, x.BountyId },
                        principalTable: "bounties",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_definitions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<long>(type: "bigint", nullable: false),
                    Key = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Scope = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    CompletionMode = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    EventRule = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Increment = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    FilterToken = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    Target = table.Column<long>(type: "bigint", nullable: false),
                    PointsReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ResetCadence = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ResetLocalTime = table.Column<string>(
                        type: "character varying(5)",
                        maxLength: 5,
                        nullable: false
                    ),
                    ResetWeekday = table.Column<int>(type: "integer", nullable: true),
                    ScheduleRevision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_definitions", x => x.Id);
                    table.UniqueConstraint(
                        "AK_community_definitions_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_community_definitions_community_seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "community_seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_community_definitions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    OperationKey = table.Column<string>(
                        type: "character varying(240)",
                        maxLength: 240,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_events_community_seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "community_seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_reward_definitions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<long>(type: "bigint", nullable: false),
                    Key = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    PresentationToken = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_reward_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_reward_definitions_community_seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "community_seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_community_reward_definitions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_season_standings",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<long>(type: "bigint", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    CompletedCount = table.Column<int>(type: "integer", nullable: false),
                    ProgressAmount = table.Column<long>(type: "bigint", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    SnapshottedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_season_standings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_season_standings_community_seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "community_seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_season_standings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competition_audits",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionId = table.Column<long>(type: "bigint", nullable: false),
                    MatchId = table.Column<long>(type: "bigint", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ActorTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateReason = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    PreviousScoreA = table.Column<int>(type: "integer", nullable: true),
                    PreviousScoreB = table.Column<int>(type: "integer", nullable: true),
                    PreviousWinnerEntrantId = table.Column<long>(type: "bigint", nullable: true),
                    NewScoreA = table.Column<int>(type: "integer", nullable: true),
                    NewScoreB = table.Column<int>(type: "integer", nullable: true),
                    NewWinnerEntrantId = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_audits_competitions_HostId_CompetitionId",
                        columns: x => new { x.HostId, x.CompetitionId },
                        principalTable: "competitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competition_entrants",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionId = table.Column<long>(type: "bigint", nullable: false),
                    RegistrationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    SeedRank = table.Column<int>(type: "integer", nullable: true),
                    RegisteredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_entrants", x => x.Id);
                    table.UniqueConstraint(
                        "AK_competition_entrants_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_competition_entrants_competitions_HostId_CompetitionId",
                        columns: x => new { x.HostId, x.CompetitionId },
                        principalTable: "competitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competition_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionId = table.Column<long>(type: "bigint", nullable: false),
                    CompetitionPublicId = table.Column<string>(
                        type: "character varying(36)",
                        nullable: false
                    ),
                    OperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_events_competitions_HostId_CompetitionId",
                        columns: x => new { x.HostId, x.CompetitionId },
                        principalTable: "competitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competition_milestone_reward_rules",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionId = table.Column<long>(type: "bigint", nullable: false),
                    WinsRequired = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    AchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_milestone_reward_rules", x => x.Id);
                    table.CheckConstraint(
                        "CK_competition_milestone_reward_rules_WinsRequired",
                        "\"WinsRequired\" > 0"
                    );
                    table.ForeignKey(
                        name: "FK_competition_milestone_reward_rules_competitions_HostId_Comp~",
                        columns: x => new { x.HostId, x.CompetitionId },
                        principalTable: "competitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competition_reward_receipts",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionId = table.Column<long>(type: "bigint", nullable: false),
                    EntrantId = table.Column<long>(type: "bigint", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    RewardKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    Placement = table.Column<int>(type: "integer", nullable: true),
                    WinsRequired = table.Column<int>(type: "integer", nullable: true),
                    PointsGranted = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    AchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    GrantedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    AchievementGrantedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_reward_receipts", x => x.Id);
                    table.CheckConstraint(
                        "CK_competition_reward_receipts_Placement",
                        "\"Placement\" IS NULL OR \"Placement\" > 0"
                    );
                    table.CheckConstraint(
                        "CK_competition_reward_receipts_WinsRequired",
                        "\"WinsRequired\" IS NULL OR \"WinsRequired\" > 0"
                    );
                    table.ForeignKey(
                        name: "FK_competition_reward_receipts_competitions_HostId_Competition~",
                        columns: x => new { x.HostId, x.CompetitionId },
                        principalTable: "competitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_command_aliases",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CustomCommandId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_command_aliases_custom_commands_HostId_CustomCommand~",
                        columns: x => new { x.HostId, x.CustomCommandId },
                        principalTable: "custom_commands",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_command_allowed_users",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CustomCommandId = table.Column<int>(type: "integer", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_custom_command_allowed_users",
                        x => new
                        {
                            x.HostId,
                            x.CustomCommandId,
                            x.TwitchUserId,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_allowed_users_custom_commands_HostId_CustomC~",
                        columns: x => new { x.HostId, x.CustomCommandId },
                        principalTable: "custom_commands",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_command_invocation_claims",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CustomCommandId = table.Column<int>(type: "integer", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    TwitchStreamId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    ClaimedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_invocation_claims", x => x.Id);
                    table.CheckConstraint(
                        "CK_custom_command_invocation_claims_Scope",
                        "(\"TwitchUserId\" IS NULL AND \"TwitchStreamId\" IS NOT NULL) OR (\"TwitchUserId\" IS NOT NULL AND \"TwitchStreamId\" IS NULL) OR (\"TwitchUserId\" IS NOT NULL AND \"TwitchStreamId\" IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_invocation_claims_custom_commands_HostId_Cus~",
                        columns: x => new { x.HostId, x.CustomCommandId },
                        principalTable: "custom_commands",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_command_invocation_reset_audits",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CustomCommandId = table.Column<int>(type: "integer", nullable: true),
                    CommandName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Scope = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    TargetTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    TargetLogin = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    AffectedClaimCount = table.Column<int>(type: "integer", nullable: false),
                    ResetAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_invocation_reset_audits", x => x.Id);
                    table.CheckConstraint(
                        "CK_custom_command_invocation_reset_audits_Scope",
                        "\"Scope\" IN ('AllViewers', 'OneViewer')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_invocation_reset_audits_custom_commands_Cust~",
                        column: x => x.CustomCommandId,
                        principalTable: "custom_commands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_invocation_reset_audits_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_announcements",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MessageLibraryEntryId = table.Column<int>(type: "integer", nullable: false),
                    DeliveryPolicyId = table.Column<int>(type: "integer", nullable: false),
                    DeliveryType = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false,
                        defaultValue: "ChatMessage"
                    ),
                    AnnouncementColor = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "Primary"
                    ),
                    LatestDeliveryResult = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "None"
                    ),
                    LastSentAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastOccurrenceAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OccurrenceStatus = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    OccurrenceDueAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OccurrenceExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OccurrenceNextAttemptAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OccurrenceCompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OccurrenceAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    OccurrenceMessage = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    ChatMessagesSinceLastSent = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_announcements", x => x.Id);
                    table.UniqueConstraint(
                        "AK_custom_announcements_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_custom_announcements_AnnouncementColor",
                        "\"AnnouncementColor\" IN ('Blue', 'Green', 'Orange', 'Primary', 'Purple')"
                    );
                    table.CheckConstraint(
                        "CK_custom_announcements_DeliveryType",
                        "\"DeliveryType\" IN ('ChatMessage', 'TwitchAnnouncement')"
                    );
                    table.CheckConstraint(
                        "CK_custom_announcements_LatestDeliveryResult",
                        "\"LatestDeliveryResult\" IN ('Ambiguous', 'Invalid', 'None', 'Permission', 'RateLimitRetry', 'Success', 'Unexpected')"
                    );
                    table.CheckConstraint(
                        "CK_custom_announcements_OccurrenceState",
                        "(\"OccurrenceStatus\" = 'None' AND \"OccurrenceDueAtUtc\" IS NULL AND \"OccurrenceExpiresAtUtc\" IS NULL AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NULL AND \"OccurrenceAttemptCount\" = 0 AND \"OccurrenceMessage\" IS NULL) OR (\"OccurrenceStatus\" = 'Pending' AND \"OccurrenceDueAtUtc\" IS NOT NULL AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" AND \"OccurrenceNextAttemptAtUtc\" IS NOT NULL AND \"OccurrenceNextAttemptAtUtc\" <= \"OccurrenceExpiresAtUtc\" AND \"OccurrenceCompletedAtUtc\" IS NULL AND \"OccurrenceAttemptCount\" = 0 AND \"OccurrenceMessage\" IS NULL) OR (\"OccurrenceStatus\" = 'Attempting' AND \"OccurrenceDueAtUtc\" IS NOT NULL AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NULL AND \"OccurrenceAttemptCount\" > 0 AND length(\"OccurrenceMessage\") > 0) OR (\"OccurrenceStatus\" = 'RetryScheduled' AND \"OccurrenceDueAtUtc\" IS NOT NULL AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" AND \"OccurrenceNextAttemptAtUtc\" >= \"OccurrenceDueAtUtc\" AND \"OccurrenceNextAttemptAtUtc\" <= \"OccurrenceExpiresAtUtc\" AND \"OccurrenceCompletedAtUtc\" IS NULL AND \"OccurrenceAttemptCount\" > 0 AND length(\"OccurrenceMessage\") > 0) OR (\"OccurrenceStatus\" IN ('Accepted', 'TerminalRejected', 'TerminalAmbiguous', 'TerminalUnexpected') AND \"OccurrenceDueAtUtc\" IS NOT NULL AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NOT NULL AND \"OccurrenceAttemptCount\" > 0 AND \"OccurrenceMessage\" IS NULL) OR (\"OccurrenceStatus\" = 'SkippedExpired' AND \"OccurrenceDueAtUtc\" IS NOT NULL AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NOT NULL AND \"OccurrenceAttemptCount\" >= 0 AND \"OccurrenceMessage\" IS NULL) OR (\"OccurrenceStatus\" = 'TerminalMissingMessage' AND \"OccurrenceDueAtUtc\" IS NOT NULL AND \"OccurrenceExpiresAtUtc\" > \"OccurrenceDueAtUtc\" AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NOT NULL AND \"OccurrenceAttemptCount\" = 0 AND \"OccurrenceMessage\" IS NULL) OR (\"OccurrenceStatus\" = 'TerminalInvalidTimeZone' AND \"OccurrenceDueAtUtc\" IS NULL AND \"OccurrenceExpiresAtUtc\" IS NULL AND \"OccurrenceNextAttemptAtUtc\" IS NULL AND \"OccurrenceCompletedAtUtc\" IS NOT NULL AND \"OccurrenceAttemptCount\" = 0 AND \"OccurrenceMessage\" IS NULL)"
                    );
                    table.CheckConstraint(
                        "CK_custom_announcements_OccurrenceStatus",
                        "\"OccurrenceStatus\" IN ('Accepted', 'Attempting', 'None', 'Pending', 'RetryScheduled', 'SkippedExpired', 'TerminalAmbiguous', 'TerminalInvalidTimeZone', 'TerminalMissingMessage', 'TerminalRejected', 'TerminalUnexpected')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_announcements_custom_announcement_delivery_policies_~",
                        columns: x => new { x.HostId, x.DeliveryPolicyId },
                        principalTable: "custom_announcement_delivery_policies",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_custom_announcements_custom_message_library_entries_HostId_~",
                        columns: x => new { x.HostId, x.MessageLibraryEntryId },
                        principalTable: "custom_message_library_entries",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_custom_announcements_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_command_actions",
                columns: table => new
                {
                    CustomCommandId = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ZeroArgumentMessageLibraryEntryId = table.Column<int>(
                        type: "integer",
                        nullable: true
                    ),
                    OneArgumentMessageLibraryEntryId = table.Column<int>(
                        type: "integer",
                        nullable: true
                    ),
                    TwoArgumentMessageLibraryEntryId = table.Column<int>(
                        type: "integer",
                        nullable: true
                    ),
                    ActionType = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    CounterId = table.Column<int>(type: "integer", nullable: true),
                    TargetOverlayPublicId = table.Column<string>(
                        type: "character varying(36)",
                        nullable: true
                    ),
                    CuePublicId = table.Column<string>(
                        type: "character varying(36)",
                        nullable: true
                    ),
                    QueuePolicy = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    ReplyOrder = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_actions", x => x.CustomCommandId);
                    table.CheckConstraint(
                        "CK_custom_command_actions_ActionType",
                        "\"ActionType\" IN ('Automation', 'Counter', 'Message', 'OverlayCue')"
                    );
                    table.CheckConstraint(
                        "CK_custom_command_actions_Payload",
                        "(\"ActionType\" IN ('Message', 'Automation') AND \"CounterId\" IS NULL AND \"TargetOverlayPublicId\" IS NULL AND \"CuePublicId\" IS NULL AND \"QueuePolicy\" IS NULL AND \"ReplyOrder\" IS NULL) OR (\"ActionType\" = 'Counter' AND \"CounterId\" IS NOT NULL AND \"TargetOverlayPublicId\" IS NULL AND \"CuePublicId\" IS NULL AND \"QueuePolicy\" IS NULL AND \"ReplyOrder\" IS NULL) OR (\"ActionType\" = 'OverlayCue' AND \"CounterId\" IS NULL AND \"TargetOverlayPublicId\" IS NOT NULL AND \"CuePublicId\" IS NOT NULL AND \"QueuePolicy\" IS NOT NULL AND \"ReplyOrder\" IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_custom_command_actions_QueuePolicy",
                        "\"QueuePolicy\" IS NULL OR \"QueuePolicy\" IN ('concurrent', 'enqueue', 'ignore', 'replace')"
                    );
                    table.CheckConstraint(
                        "CK_custom_command_actions_ReplyOrder",
                        "\"ReplyOrder\" IS NULL OR \"ReplyOrder\" IN ('after', 'before')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_actions_custom_commands_HostId_CustomCommand~",
                        columns: x => new { x.HostId, x.CustomCommandId },
                        principalTable: "custom_commands",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_actions_custom_counters_HostId_CounterId",
                        columns: x => new { x.HostId, x.CounterId },
                        principalTable: "custom_counters",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_actions_custom_message_library_entries_HostI~",
                        columns: x => new { x.HostId, x.OneArgumentMessageLibraryEntryId },
                        principalTable: "custom_message_library_entries",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_actions_custom_message_library_entries_Host~1",
                        columns: x => new { x.HostId, x.TwoArgumentMessageLibraryEntryId },
                        principalTable: "custom_message_library_entries",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_actions_custom_message_library_entries_Host~2",
                        columns: x => new { x.HostId, x.ZeroArgumentMessageLibraryEntryId },
                        principalTable: "custom_message_library_entries",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_message_variants",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CustomMessageLibraryEntryId = table.Column<int>(
                        type: "integer",
                        nullable: false
                    ),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_message_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_message_variants_custom_message_library_entries_Cust~",
                        column: x => x.CustomMessageLibraryEntryId,
                        principalTable: "custom_message_library_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "command_aliases",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GuessRoundProfileId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Alias = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_aliases", x => x.Id);
                    table.CheckConstraint(
                        "CK_command_aliases_Kind",
                        "\"Kind\" IN ('AddPoints', 'CancelGiveaway', 'Commands', 'EndGiveaway', 'Gamble', 'Giveaway', 'GivePoints', 'Guess', 'Guesses', 'Join', 'Points', 'RemovePoints', 'Start', 'Stop', 'Win')"
                    );
                    table.ForeignKey(
                        name: "FK_command_aliases_guess_round_profiles_HostId_GuessRoundProfi~",
                        columns: x => new { x.HostId, x.GuessRoundProfileId },
                        principalTable: "guess_round_profiles",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_command_aliases_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "guess_options",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    GuessRoundProfileId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ReplyText = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(
                        type: "integer",
                        nullable: false,
                        defaultValue: 0
                    ),
                    ReplyTarget = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false,
                        defaultValue: "chat"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guess_options", x => x.Id);
                    table.CheckConstraint(
                        "CK_guess_options_ReplyTarget",
                        "\"ReplyTarget\" IN ('chat', 'whisper')"
                    );
                    table.ForeignKey(
                        name: "FK_guess_options_guess_round_profiles_GuessRoundProfileId",
                        column: x => x.GuessRoundProfileId,
                        principalTable: "guess_round_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "guess_rounds",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GuessRoundProfileId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ClosedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    WinningName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guess_rounds", x => x.Id);
                    table.CheckConstraint(
                        "CK_guess_rounds_Status",
                        "\"Status\" IN ('Closed', 'Completed', 'Open')"
                    );
                    table.ForeignKey(
                        name: "FK_guess_rounds_guess_round_profiles_GuessRoundProfileId",
                        column: x => x.GuessRoundProfileId,
                        principalTable: "guess_round_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_guess_rounds_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "reply_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    GuessRoundProfileId = table.Column<int>(type: "integer", nullable: false),
                    RoundStartedReply = table.Column<string>(type: "text", nullable: false),
                    RoundAlreadyOpenReply = table.Column<string>(type: "text", nullable: false),
                    NoOpenRoundReply = table.Column<string>(type: "text", nullable: false),
                    GuessingStoppedReply = table.Column<string>(type: "text", nullable: false),
                    GuessingAlreadyStoppedReply = table.Column<string>(
                        type: "text",
                        nullable: false
                    ),
                    GuessingClosedReply = table.Column<string>(type: "text", nullable: false),
                    InvalidGuessReply = table.Column<string>(type: "text", nullable: false),
                    GuessUsageReply = table.Column<string>(type: "text", nullable: false),
                    AvailableGuessesReply = table.Column<string>(type: "text", nullable: false),
                    WinUsageReply = table.Column<string>(type: "text", nullable: false),
                    ModeratorOnlyReply = table.Column<string>(type: "text", nullable: false),
                    WinnerReply = table.Column<string>(type: "text", nullable: false),
                    NoWinnersReply = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reply_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reply_settings_guess_round_profiles_GuessRoundProfileId",
                        column: x => x.GuessRoundProfileId,
                        principalTable: "guess_round_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_event_feed_items",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    OverlayInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    SourceKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Priority = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    Lifecycle = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Body = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    EnqueuedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DisplayDeadlineUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    TombstoneExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_event_feed_items", x => x.Id);
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Duration",
                        "\"DurationSeconds\" BETWEEN 1 AND 30"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Kind",
                        "\"Kind\" IN ('achievementCompletion', 'bingoEvent', 'giveawayWinner', 'guessingWinner', 'pointAward')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Lifecycle",
                        "\"Lifecycle\" IN ('active', 'consumed', 'queued', 'suppressed')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Priority",
                        "\"Priority\" IN ('high', 'normal')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_SourceKey",
                        "length(\"SourceKey\") BETWEEN 1 AND 160"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Text",
                        "length(\"Title\") BETWEEN 1 AND 160 AND length(\"Body\") >= 1"
                    );
                    table.ForeignKey(
                        name: "FK_overlay_event_feed_items_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_overlay_event_feed_items_overlay_instances_OverlayInstanceId",
                        column: x => x.OverlayInstanceId,
                        principalTable: "overlay_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "play_queue_entries",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    NormalizedLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    JoinedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ReadyExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    PartyNumber = table.Column<int>(type: "integer", nullable: true),
                    PrivateModeratorNote = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_entries", x => x.Id);
                    table.CheckConstraint(
                        "CK_play_queue_entries_Status",
                        "\"Status\" IN ('AwaitingReady', 'Left', 'NoShow', 'Ready', 'Selected', 'Skipped', 'Waiting')"
                    );
                    table.ForeignKey(
                        name: "FK_play_queue_entries_play_queues_QueueId",
                        column: x => x.QueueId,
                        principalTable: "play_queues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "play_queue_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    EntryId = table.Column<long>(type: "bigint", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_play_queue_events_Kind",
                        "\"Kind\" IN ('Joined', 'Left', 'NoShow', 'PartySelected', 'QueueClosed', 'QueueConfigured', 'Ready', 'ReadyCheckStarted', 'Skipped')"
                    );
                    table.ForeignKey(
                        name: "FK_play_queue_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_play_queue_events_play_queues_QueueId",
                        column: x => x.QueueId,
                        principalTable: "play_queues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "play_queue_exclusions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    ExpiresAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    PrivateReason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_exclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_play_queue_exclusions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_play_queue_exclusions_play_queues_QueueId",
                        column: x => x.QueueId,
                        principalTable: "play_queues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "play_queue_fields",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Label = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Choices = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_fields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_play_queue_fields_play_queues_QueueId",
                        column: x => x.QueueId,
                        principalTable: "play_queues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "play_queue_participation",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    ParticipatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_participation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_play_queue_participation_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_play_queue_participation_play_queues_QueueId",
                        column: x => x.QueueId,
                        principalTable: "play_queues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "play_queue_role_requirements",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    MinimumCount = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_role_requirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_play_queue_role_requirements_play_queues_QueueId",
                        column: x => x.QueueId,
                        principalTable: "play_queues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_feature_secrets",
                columns: table => new
                {
                    PluginId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    FeatureId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SettingId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ProtectedValue = table.Column<byte[]>(type: "bytea", nullable: false),
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
                        name: "FK_plugin_feature_secrets_plugin_feature_configurations_Plugin~",
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
                name: "points_giveaway_entrants",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    GiveawayId = table.Column<int>(type: "integer", nullable: false),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    JoinedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_giveaway_entrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_points_giveaway_entrants_points_giveaways_GiveawayId",
                        column: x => x.GiveawayId,
                        principalTable: "points_giveaways",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "points_giveaway_winners",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    GiveawayId = table.Column<int>(type: "integer", nullable: false),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Payout = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_giveaway_winners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_points_giveaway_winners_points_giveaways_GiveawayId",
                        column: x => x.GiveawayId,
                        principalTable: "points_giveaways",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_board_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    SubmissionId = table.Column<long>(type: "bigint", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_board_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_request_board_events_Kind",
                        "\"Kind\" IN ('BoardConfigured', 'Merged', 'PointsRefunded', 'PointsReserved', 'StatusChanged', 'Submitted', 'Voted')"
                    );
                    table.ForeignKey(
                        name: "FK_request_board_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_request_board_events_request_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "request_boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_board_fields",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(
                        type: "character varying(48)",
                        maxLength: 48,
                        nullable: false
                    ),
                    Label = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    MaximumLength = table.Column<int>(type: "integer", nullable: false),
                    MinimumNumber = table.Column<decimal>(type: "numeric", nullable: true),
                    MaximumNumber = table.Column<decimal>(type: "numeric", nullable: true),
                    ChoiceOptions = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_board_fields", x => x.Id);
                    table.CheckConstraint(
                        "CK_request_board_fields_Kind",
                        "\"Kind\" IN ('Choice', 'Number', 'Text', 'TwitchClip', 'Url')"
                    );
                    table.ForeignKey(
                        name: "FK_request_board_fields_request_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "request_boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_submissions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    BoardId = table.Column<int>(type: "integer", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmitterLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    NormalizedTitle = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    NormalizedUrl = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: true
                    ),
                    Status = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    Category = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Tags = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    QueuePosition = table.Column<long>(type: "bigint", nullable: false),
                    VoteCount = table.Column<int>(type: "integer", nullable: false),
                    PublicNote = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    PrivateModeratorNote = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    PrivateRejectionReason = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    PointReservationState = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    MergedIntoSubmissionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_submissions", x => x.Id);
                    table.CheckConstraint(
                        "CK_request_submissions_PointReservationState",
                        "\"PointReservationState\" IN ('Consumed', 'None', 'Refunded', 'Reserved')"
                    );
                    table.CheckConstraint(
                        "CK_request_submissions_Status",
                        "\"Status\" IN ('Accepted', 'Approved', 'Completed', 'Merged', 'Pending', 'Queued', 'Rejected', 'Withdrawn')"
                    );
                    table.ForeignKey(
                        name: "FK_request_submissions_request_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "request_boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_request_submissions_request_submissions_MergedIntoSubmissio~",
                        column: x => x.MergedIntoSubmissionId,
                        principalTable: "request_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_poll_template_choices",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    TwitchPollTemplateId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(
                        type: "character varying(25)",
                        maxLength: 25,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_poll_template_choices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_poll_template_choices_twitch_poll_templates_TwitchPo~",
                        column: x => x.TwitchPollTemplateId,
                        principalTable: "twitch_poll_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "twitch_prediction_template_outcomes",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    TwitchPredictionTemplateId = table.Column<int>(
                        type: "integer",
                        nullable: false
                    ),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(
                        type: "character varying(25)",
                        maxLength: 25,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_prediction_template_outcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_prediction_template_outcomes_twitch_prediction_templ~",
                        column: x => x.TwitchPredictionTemplateId,
                        principalTable: "twitch_prediction_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_candidates",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    StreamIdentity = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    State = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    TwitchClipId = table.Column<int>(type: "integer", nullable: true),
                    TwitchStreamMarkerId = table.Column<int>(type: "integer", nullable: true),
                    PublicTitle = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    PublicCategory = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    ProviderFailureReason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    PrivateRejectionReason = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    MergedIntoCandidateId = table.Column<long>(type: "bigint", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastCapturedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ApprovedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    RejectedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_candidates", x => x.Id);
                    table.UniqueConstraint(
                        "AK_moment_candidates_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_moment_candidates_State",
                        "\"State\" IN ('Approved', 'ClipReady', 'Failed', 'MarkerReady', 'Merged', 'ProviderPending', 'Rejected')"
                    );
                    table.ForeignKey(
                        name: "FK_moment_candidates_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_candidates_moment_candidates_MergedIntoCandidateId",
                        column: x => x.MergedIntoCandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_moment_candidates_twitch_clips_TwitchClipId",
                        column: x => x.TwitchClipId,
                        principalTable: "twitch_clips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_moment_candidates_twitch_stream_markers_TwitchStreamMarkerId",
                        column: x => x.TwitchStreamMarkerId,
                        principalTable: "twitch_stream_markers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "whisper_quota_recipients",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    WhisperQuotaBucketId = table.Column<int>(type: "integer", nullable: false),
                    RecipientTwitchUserId = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    RecipientLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    FirstSentAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whisper_quota_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whisper_quota_recipients_whisper_quota_buckets_WhisperQuota~",
                        column: x => x.WhisperQuotaBucketId,
                        principalTable: "whisper_quota_buckets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_cue_media_asset_references",
                columns: table => new
                {
                    CueId = table.Column<long>(type: "bigint", nullable: false),
                    AssetId = table.Column<long>(type: "bigint", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_overlay_cue_media_asset_references",
                        x => new { x.CueId, x.AssetId }
                    );
                    table.ForeignKey(
                        name: "FK_overlay_cue_media_asset_references_overlay_cues_CueId_HostId",
                        columns: x => new { x.CueId, x.HostId },
                        principalTable: "overlay_cues",
                        principalColumns: new[] { "Id", "HostId" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_overlay_cue_media_asset_references_overlay_media_assets_Ass~",
                        columns: x => new { x.AssetId, x.HostId },
                        principalTable: "overlay_media_assets",
                        principalColumns: new[] { "Id", "HostId" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "plugin_marketplace_catalog_media",
                columns: table => new
                {
                    PluginId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    DeclaredVersion = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: false
                    ),
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
                        name: "FK_plugin_marketplace_catalog_media_plugin_marketplace_catalog~",
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
                    PluginId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    DeclaredVersion = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
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
                        name: "FK_plugin_marketplace_catalog_tags_plugin_marketplace_catalog_~",
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
                    PluginId = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    DeclaredVersion = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    MutableTag = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
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
                        name: "FK_plugin_marketplace_catalog_targets_plugin_marketplace_catal~",
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
                name: "automation_node_runs",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    AvailableAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OutcomeCode = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    OutputJson = table.Column<string>(type: "text", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_node_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_automation_node_runs_automation_flow_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "automation_flow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_games",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateRevisionId = table.Column<long>(type: "bigint", nullable: false),
                    TemplateName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    TemplateRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Mode = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ParticipantCap = table.Column<int>(type: "integer", nullable: true),
                    TeamCap = table.Column<int>(type: "integer", nullable: true),
                    RosterRevision = table.Column<long>(type: "bigint", nullable: false),
                    FullCardWinEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LinePointsReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    LineAchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: true
                    ),
                    FullCardPointsReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    FullCardAchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    IssuedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ArchivedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_games", x => x.Id);
                    table.CheckConstraint("CK_bingo_games_Dimension", "\"Dimension\" IN (3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_bingo_games_bingo_template_revisions_TemplateRevisionId",
                        column: x => x.TemplateRevisionId,
                        principalTable: "bingo_template_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_bingo_games_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_squares",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    TemplateRevisionId = table.Column<long>(type: "bigint", nullable: false),
                    Key = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(
                        type: "character varying(240)",
                        maxLength: 240,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Threshold = table.Column<long>(type: "bigint", nullable: true),
                    FilterToken = table.Column<string>(
                        type: "character varying(240)",
                        maxLength: 240,
                        nullable: true
                    ),
                    PrivateModeratorNote = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_squares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_squares_bingo_template_revisions_TemplateRevisionId",
                        column: x => x.TemplateRevisionId,
                        principalTable: "bingo_template_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_audits",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<long>(type: "bigint", nullable: true),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    OperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    ActorTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateNote = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_audits_community_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "community_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_audits_community_seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "community_seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_audits_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_completions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    SubjectKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    DefinitionKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    DefinitionName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PeriodKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    PointsGranted = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    RewardSnapshot = table.Column<string>(
                        type: "character varying(4000)",
                        maxLength: 4000,
                        nullable: false
                    ),
                    SourceOperationKey = table.Column<string>(
                        type: "character varying(240)",
                        maxLength: 240,
                        nullable: false
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_completions", x => x.Id);
                    table.UniqueConstraint(
                        "AK_community_completions_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_community_completions_community_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "community_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_completions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_progress",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SeasonId = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    SubjectKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    CompletionCount = table.Column<int>(type: "integer", nullable: false),
                    PeriodKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_progress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_progress_community_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "community_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_community_progress_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_reset_periods",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    RolloverKind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    OperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ClosedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_reset_periods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_reset_periods_community_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "community_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_community_reset_periods_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_definition_rewards",
                columns: table => new
                {
                    DefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    RewardDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_community_definition_rewards",
                        x => new { x.DefinitionId, x.RewardDefinitionId }
                    );
                    table.ForeignKey(
                        name: "FK_community_definition_rewards_community_definitions_Definiti~",
                        column: x => x.DefinitionId,
                        principalTable: "community_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_community_definition_rewards_community_reward_definitions_R~",
                        column: x => x.RewardDefinitionId,
                        principalTable: "community_reward_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_equipped_rewards",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    RewardDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    LastOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquippedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_equipped_rewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_equipped_rewards_community_reward_definitions_Rew~",
                        column: x => x.RewardDefinitionId,
                        principalTable: "community_reward_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_equipped_rewards_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "viewer_passports",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    ProfileLine = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    Visibility = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    HideAttendance = table.Column<bool>(type: "boolean", nullable: false),
                    SelectedTitleRewardDefinitionId = table.Column<long>(
                        type: "bigint",
                        nullable: true
                    ),
                    SelectedBadgeRewardDefinitionId = table.Column<long>(
                        type: "bigint",
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passports", x => x.Id);
                    table.UniqueConstraint(
                        "AK_viewer_passports_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passports_community_reward_definitions_SelectedBadge~",
                        column: x => x.SelectedBadgeRewardDefinitionId,
                        principalTable: "community_reward_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passports_community_reward_definitions_SelectedTitle~",
                        column: x => x.SelectedTitleRewardDefinitionId,
                        principalTable: "community_reward_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passports_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competition_entrant_members",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionEntrantId = table.Column<long>(type: "bigint", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateContact = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_entrant_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_entrant_members_competition_entrants_HostId_Com~",
                        columns: x => new { x.HostId, x.CompetitionEntrantId },
                        principalTable: "competition_entrants",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competition_matches",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CompetitionId = table.Column<long>(type: "bigint", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    EntrantAId = table.Column<long>(type: "bigint", nullable: true),
                    EntrantBId = table.Column<long>(type: "bigint", nullable: true),
                    ScoreA = table.Column<int>(type: "integer", nullable: true),
                    ScoreB = table.Column<int>(type: "integer", nullable: true),
                    WinnerEntrantId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ScheduledAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ReminderDueAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ReminderDeliveredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ReminderSuppressedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ConfirmedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_matches", x => x.Id);
                    table.UniqueConstraint(
                        "AK_competition_matches_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint("CK_competition_matches_Position", "\"Position\" >= 0");
                    table.CheckConstraint("CK_competition_matches_Round", "\"Round\" > 0");
                    table.CheckConstraint(
                        "CK_competition_matches_ScoreA",
                        "\"ScoreA\" IS NULL OR \"ScoreA\" >= 0"
                    );
                    table.CheckConstraint(
                        "CK_competition_matches_ScoreB",
                        "\"ScoreB\" IS NULL OR \"ScoreB\" >= 0"
                    );
                    table.ForeignKey(
                        name: "FK_competition_matches_competition_entrants_HostId_EntrantAId",
                        columns: x => new { x.HostId, x.EntrantAId },
                        principalTable: "competition_entrants",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_competition_matches_competition_entrants_HostId_EntrantBId",
                        columns: x => new { x.HostId, x.EntrantBId },
                        principalTable: "competition_entrants",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_competition_matches_competition_entrants_HostId_WinnerEntra~",
                        columns: x => new { x.HostId, x.WinnerEntrantId },
                        principalTable: "competition_entrants",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_competition_matches_competitions_HostId_CompetitionId",
                        columns: x => new { x.HostId, x.CompetitionId },
                        principalTable: "competitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_announcement_schedules",
                columns: table => new
                {
                    CustomAnnouncementId = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ScheduleType = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    IntervalMinutes = table.Column<int>(type: "integer", nullable: true),
                    RequiredChatMessages = table.Column<int>(type: "integer", nullable: true),
                    WeeklyDay = table.Column<int>(type: "integer", nullable: true),
                    WeeklyTime = table.Column<TimeOnly>(
                        type: "time without time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_custom_announcement_schedules",
                        x => x.CustomAnnouncementId
                    );
                    table.CheckConstraint(
                        "CK_custom_announcement_schedules_Payload",
                        "(\"ScheduleType\" = 'Interval' AND \"IntervalMinutes\" >= 1 AND \"RequiredChatMessages\" IS NULL AND \"WeeklyDay\" IS NULL AND \"WeeklyTime\" IS NULL) OR (\"ScheduleType\" = 'IntervalAfterChat' AND \"IntervalMinutes\" >= 1 AND \"RequiredChatMessages\" >= 1 AND \"WeeklyDay\" IS NULL AND \"WeeklyTime\" IS NULL) OR (\"ScheduleType\" = 'Weekly' AND \"IntervalMinutes\" IS NULL AND \"RequiredChatMessages\" IS NULL AND \"WeeklyDay\" BETWEEN 0 AND 6 AND \"WeeklyTime\" IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_custom_announcement_schedules_ScheduleType",
                        "\"ScheduleType\" IN ('Interval', 'IntervalAfterChat', 'Weekly')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_announcement_schedules_custom_announcements_HostId_C~",
                        columns: x => new { x.HostId, x.CustomAnnouncementId },
                        principalTable: "custom_announcements",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "guess_votes",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    GuessRoundId = table.Column<int>(type: "integer", nullable: false),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    GuessName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    GuessedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guess_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_guess_votes_guess_rounds_GuessRoundId",
                        column: x => x.GuessRoundId,
                        principalTable: "guess_rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "play_queue_entry_values",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    EntryId = table.Column<long>(type: "bigint", nullable: false),
                    FieldId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_entry_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_play_queue_entry_values_play_queue_entries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "play_queue_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_play_queue_entry_values_play_queue_fields_FieldId",
                        column: x => x.FieldId,
                        principalTable: "play_queue_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_submission_values",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    SubmissionId = table.Column<long>(type: "bigint", nullable: false),
                    FieldId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_submission_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_request_submission_values_request_board_fields_FieldId",
                        column: x => x.FieldId,
                        principalTable: "request_board_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_request_submission_values_request_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "request_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_submission_votes",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    SubmissionId = table.Column<long>(type: "bigint", nullable: false),
                    VoterLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_submission_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_request_submission_votes_request_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "request_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_capture_requests",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CandidateId = table.Column<long>(type: "bigint", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    CapturedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_capture_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_capture_requests_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_contributors",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CandidateId = table.Column<long>(type: "bigint", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    NormalizedLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CaptureCount = table.Column<int>(type: "integer", nullable: false),
                    FirstCapturedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastCapturedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_contributors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_contributors_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CandidateId = table.Column<long>(type: "bigint", nullable: false),
                    OperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    StreamIdentity = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_moment_events_Kind",
                        "\"Kind\" IN ('Approved', 'Captured', 'Winner')"
                    );
                    table.ForeignKey(
                        name: "FK_moment_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_events_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_merges",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SourceCandidateId = table.Column<long>(type: "bigint", nullable: false),
                    TargetCandidateId = table.Column<long>(type: "bigint", nullable: false),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateText = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    MergedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_merges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_merges_moment_candidates_SourceCandidateId",
                        column: x => x.SourceCandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_moment_merges_moment_candidates_TargetCandidateId",
                        column: x => x.TargetCandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_moderation_audit",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CandidateId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateText = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_moderation_audit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_moderation_audit_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_suggestions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CandidateId = table.Column<long>(type: "bigint", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    SuggestedTitle = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    SuggestedCategory = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_suggestions_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_votes",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CandidateId = table.Column<long>(type: "bigint", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    NormalizedLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_votes_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_weekly_finalizations",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    WeekStartsAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    WinningCandidateId = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_weekly_finalizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_weekly_finalizations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_weekly_finalizations_moment_candidates_WinningCandid~",
                        column: x => x.WinningCandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_cards",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    AssignmentKey = table.Column<string>(
                        type: "character varying(240)",
                        maxLength: 240,
                        nullable: false
                    ),
                    AssignmentName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    IssuedLayout = table.Column<string>(
                        type: "character varying(16000)",
                        maxLength: 16000,
                        nullable: true
                    ),
                    IssuedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_cards_bingo_games_GameId",
                        column: x => x.GameId,
                        principalTable: "bingo_games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_teams",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_teams_bingo_games_GameId",
                        column: x => x.GameId,
                        principalTable: "bingo_games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_external_grant_receipts",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    IdempotencyKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    Fingerprint = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CompletionId = table.Column<long>(type: "bigint", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_external_grant_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_external_grant_receipts_community_completions_Hos~",
                        columns: x => new { x.HostId, x.CompletionId },
                        principalTable: "community_completions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_external_grant_receipts_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "community_reward_unlocks",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    RewardDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    CompletionId = table.Column<long>(type: "bigint", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_reward_unlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_reward_unlocks_community_completions_HostId_Compl~",
                        columns: x => new { x.HostId, x.CompletionId },
                        principalTable: "community_completions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_reward_unlocks_community_reward_definitions_Rewar~",
                        column: x => x.RewardDefinitionId,
                        principalTable: "community_reward_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_reward_unlocks_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "point_ledger_entries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Delta = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    BalanceAfter = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    CounterpartyLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    GiveawayId = table.Column<int>(type: "integer", nullable: true),
                    RequestSubmissionId = table.Column<long>(type: "bigint", nullable: true),
                    BountyPledgeId = table.Column<long>(type: "bigint", nullable: true),
                    BountyRewardId = table.Column<long>(type: "bigint", nullable: true),
                    CommunityCompletionId = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: false),
                    OperationKey = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_ledger_entries", x => x.Id);
                    table.CheckConstraint(
                        "CK_point_ledger_entries_Kind",
                        "\"Kind\" IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward', 'BingoReward', 'CompetitionReward', 'BlokeRaidSpecialSpend', 'BlokeRaidVictoryReward')"
                    );
                    table.ForeignKey(
                        name: "FK_point_ledger_entries_bounty_contributor_rewards_HostId_Boun~",
                        columns: x => new { x.HostId, x.BountyRewardId },
                        principalTable: "bounty_contributor_rewards",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_point_ledger_entries_bounty_pledges_HostId_BountyPledgeId",
                        columns: x => new { x.HostId, x.BountyPledgeId },
                        principalTable: "bounty_pledges",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_point_ledger_entries_community_completions_HostId_Community~",
                        columns: x => new { x.HostId, x.CommunityCompletionId },
                        principalTable: "community_completions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_point_ledger_entries_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "viewer_passport_logins",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    PassportId = table.Column<long>(type: "bigint", nullable: false),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    FirstSeenAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastSeenAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_logins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passport_logins_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passport_logins_viewer_passports_PassportId",
                        column: x => x.PassportId,
                        principalTable: "viewer_passports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "viewer_passport_stream_attendance",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    PassportId = table.Column<long>(type: "bigint", nullable: false),
                    StreamSessionId = table.Column<long>(type: "bigint", nullable: false),
                    ContinuityGeneration = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_stream_attendance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passport_stream_attendance_viewer_passport_stream_se~",
                        columns: x => new { x.HostId, x.StreamSessionId },
                        principalTable: "viewer_passport_stream_sessions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passport_stream_attendance_viewer_passports_HostId_P~",
                        columns: x => new { x.HostId, x.PassportId },
                        principalTable: "viewer_passports",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_attachments",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    MomentCandidateId = table.Column<long>(type: "bigint", nullable: false),
                    BountyId = table.Column<long>(type: "bigint", nullable: true),
                    CommunityDefinitionId = table.Column<long>(type: "bigint", nullable: true),
                    CompetitionMatchId = table.Column<long>(type: "bigint", nullable: true),
                    AttachedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_attachments", x => x.Id);
                    table.CheckConstraint(
                        "CK_moment_attachments_OneDestination",
                        "(\"BountyId\" IS NOT NULL AND \"CommunityDefinitionId\" IS NULL AND \"CompetitionMatchId\" IS NULL) OR (\"BountyId\" IS NULL AND \"CommunityDefinitionId\" IS NOT NULL AND \"CompetitionMatchId\" IS NULL) OR (\"BountyId\" IS NULL AND \"CommunityDefinitionId\" IS NULL AND \"CompetitionMatchId\" IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_moment_attachments_bounties_HostId_BountyId",
                        columns: x => new { x.HostId, x.BountyId },
                        principalTable: "bounties",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_attachments_community_definitions_HostId_CommunityDe~",
                        columns: x => new { x.HostId, x.CommunityDefinitionId },
                        principalTable: "community_definitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_attachments_competition_matches_HostId_CompetitionMa~",
                        columns: x => new { x.HostId, x.CompetitionMatchId },
                        principalTable: "competition_matches",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_attachments_moment_candidates_HostId_MomentCandidate~",
                        columns: x => new { x.HostId, x.MomentCandidateId },
                        principalTable: "moment_candidates",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_marks",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    CardId = table.Column<long>(type: "bigint", nullable: false),
                    SquareKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FirstMarkedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ChangedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_marks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_marks_bingo_cards_CardId",
                        column: x => x.CardId,
                        principalTable: "bingo_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_wins",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    CardId = table.Column<long>(type: "bigint", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(36)", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    RuleIndex = table.Column<int>(type: "integer", nullable: false),
                    RuleKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false
                    ),
                    PointsReward = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    AchievementKey = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    RewardsCompletedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_wins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_wins_bingo_cards_CardId",
                        column: x => x.CardId,
                        principalTable: "bingo_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_bingo_wins_bingo_games_GameId",
                        column: x => x.GameId,
                        principalTable: "bingo_games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_participants",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    TeamId = table.Column<long>(type: "bigint", nullable: true),
                    CardId = table.Column<long>(type: "bigint", nullable: true),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    JoinedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_participants_bingo_cards_CardId",
                        column: x => x.CardId,
                        principalTable: "bingo_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_bingo_participants_bingo_games_GameId",
                        column: x => x.GameId,
                        principalTable: "bingo_games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_bingo_participants_bingo_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "bingo_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_evidence",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    CardId = table.Column<long>(type: "bigint", nullable: false),
                    MarkId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Source = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    EventKind = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Summary = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    ParticipantTwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ParticipantLogin = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ParticipantDisplayName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: true
                    ),
                    OccurredAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    RecordedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_evidence_bingo_marks_MarkId",
                        column: x => x.MarkId,
                        principalTable: "bingo_marks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "bingo_win_recipients",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    WinId = table.Column<long>(type: "bigint", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(160)",
                        maxLength: 160,
                        nullable: false
                    ),
                    PointsGranted = table.Column<bool>(type: "boolean", nullable: false),
                    AchievementGranted = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_win_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bingo_win_recipients_bingo_wins_WinId",
                        column: x => x.WinId,
                        principalTable: "bingo_wins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_active_public_chat_pins_HostId_Channel",
                table: "active_public_chat_pins",
                columns: new[] { "HostId", "Channel" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_approved_raid_channels_HostId_Login",
                table: "approved_raid_channels",
                columns: new[] { "HostId", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automatic_raid_processed_events_ExpiresAtUtc",
                table: "automatic_raid_processed_events",
                column: "ExpiresAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_automatic_raid_processed_events_HostId_ProviderMessageId",
                table: "automatic_raid_processed_events",
                columns: new[] { "HostId", "ProviderMessageId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automatic_raid_shoutout_outcomes_HostId_CompletedAtUtc",
                table: "automatic_raid_shoutout_outcomes",
                columns: new[] { "HostId", "CompletedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automatic_raid_shoutout_outcomes_HostId_ProviderMessageId",
                table: "automatic_raid_shoutout_outcomes",
                columns: new[] { "HostId", "ProviderMessageId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automatic_raid_shoutout_settings_HostId",
                table: "automatic_raid_shoutout_settings",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_event_receipts_ExpiresAtUtc",
                table: "automation_event_receipts",
                column: "ExpiresAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_edges_FlowId_TargetNodeId",
                table: "automation_flow_edges",
                columns: new[] { "FlowId", "TargetNodeId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_nodes_FlowId_DefinitionId",
                table: "automation_flow_nodes",
                columns: new[] { "FlowId", "DefinitionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_runs_FlowId_SourceNodeId_SourceOccurrenceId",
                table: "automation_flow_runs",
                columns: new[] { "FlowId", "SourceNodeId", "SourceOccurrenceId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flow_runs_HostId_Status",
                table: "automation_flow_runs",
                columns: new[] { "HostId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_flows_HostId_IsEnabled",
                table: "automation_flows",
                columns: new[] { "HostId", "IsEnabled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_node_runs_RunId_NodeId",
                table: "automation_node_runs",
                columns: new[] { "RunId", "NodeId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_node_runs_Status_AvailableAtUtc",
                table: "automation_node_runs",
                columns: new[] { "Status", "AvailableAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_cards_GameId_AssignmentKey",
                table: "bingo_cards",
                columns: new[] { "GameId", "AssignmentKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_cards_GameId_PublicId",
                table: "bingo_cards",
                columns: new[] { "GameId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_event_receipts_HostId_Kind_SourceEventId",
                table: "bingo_event_receipts",
                columns: new[] { "HostId", "Kind", "SourceEventId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_events_HostId_OperationKey",
                table: "bingo_events",
                columns: new[] { "HostId", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_evidence_MarkId",
                table: "bingo_evidence",
                column: "MarkId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_games_HostId",
                table: "bingo_games",
                column: "HostId",
                unique: true,
                filter: "\"Status\" IN ('Joining', 'Issued')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_games_HostId_CreationOperationId",
                table: "bingo_games",
                columns: new[] { "HostId", "CreationOperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_games_HostId_PublicId",
                table: "bingo_games",
                columns: new[] { "HostId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_games_HostId_Status",
                table: "bingo_games",
                columns: new[] { "HostId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_games_TemplateRevisionId",
                table: "bingo_games",
                column: "TemplateRevisionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_marks_CardId_SquareKey",
                table: "bingo_marks",
                columns: new[] { "CardId", "SquareKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_moderation_audit_HostId_OperationId",
                table: "bingo_moderation_audit",
                columns: new[] { "HostId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_participants_CardId",
                table: "bingo_participants",
                column: "CardId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_participants_GameId_TwitchUserId",
                table: "bingo_participants",
                columns: new[] { "GameId", "TwitchUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_participants_TeamId",
                table: "bingo_participants",
                column: "TeamId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_squares_TemplateRevisionId_Key",
                table: "bingo_squares",
                columns: new[] { "TemplateRevisionId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_squares_TemplateRevisionId_SortOrder",
                table: "bingo_squares",
                columns: new[] { "TemplateRevisionId", "SortOrder" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_teams_GameId_Name",
                table: "bingo_teams",
                columns: new[] { "GameId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_teams_GameId_PublicId",
                table: "bingo_teams",
                columns: new[] { "GameId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_template_revisions_HostId_OperationId",
                table: "bingo_template_revisions",
                columns: new[] { "HostId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_template_revisions_TemplateId_Revision",
                table: "bingo_template_revisions",
                columns: new[] { "TemplateId", "Revision" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_templates_HostId_CreationOperationId",
                table: "bingo_templates",
                columns: new[] { "HostId", "CreationOperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_templates_HostId_PublicId",
                table: "bingo_templates",
                columns: new[] { "HostId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_win_recipients_WinId_TwitchUserId",
                table: "bingo_win_recipients",
                columns: new[] { "WinId", "TwitchUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_wins_CardId_RuleKey",
                table: "bingo_wins",
                columns: new[] { "CardId", "RuleKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_wins_GameId",
                table: "bingo_wins",
                column: "GameId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_wins_HostId_PublicId",
                table: "bingo_wins",
                columns: new[] { "HostId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_actions_HostId_CampaignId_ViewerTwitchUserId_Kin~",
                table: "bloke_raid_actions",
                columns: new[]
                {
                    "HostId",
                    "CampaignId",
                    "ViewerTwitchUserId",
                    "Kind",
                    "OccurredAtUtc",
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_actions_HostId_OperationKey",
                table: "bloke_raid_actions",
                columns: new[] { "HostId", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_campaigns_HostId",
                table: "bloke_raid_campaigns",
                column: "HostId",
                unique: true,
                filter: "\"Status\" = 'Active'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_campaigns_HostId_StartOperationKey",
                table: "bloke_raid_campaigns",
                columns: new[] { "HostId", "StartOperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_campaigns_PublicId",
                table: "bloke_raid_campaigns",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_configurations_HostId",
                table: "bloke_raid_configurations",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_contributions_HostId_CampaignId_ViewerTwitchUser~",
                table: "bloke_raid_contributions",
                columns: new[] { "HostId", "CampaignId", "ViewerTwitchUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_events_HostId_CampaignId",
                table: "bloke_raid_events",
                columns: new[] { "HostId", "CampaignId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_events_HostId_OperationKey",
                table: "bloke_raid_events",
                columns: new[] { "HostId", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounties_HostId_CreationOperationId",
                table: "bounties",
                columns: new[] { "HostId", "CreationOperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounties_PublicId",
                table: "bounties",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounties_Status_ExpiresAtUtc_Id",
                table: "bounties",
                columns: new[] { "Status", "ExpiresAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounty_contributor_rewards_HostId_BountyId_Login",
                table: "bounty_contributor_rewards",
                columns: new[] { "HostId", "BountyId", "Login" },
                unique: true,
                filter: "\"Login\" <> '[erased]'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounty_events_HostId_BountyId",
                table: "bounty_events",
                columns: new[] { "HostId", "BountyId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounty_events_HostId_OperationKey",
                table: "bounty_events",
                columns: new[] { "HostId", "OperationKey" },
                unique: true,
                filter: "\"OperationKey\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounty_moderation_audit_HostId_BountyId",
                table: "bounty_moderation_audit",
                columns: new[] { "HostId", "BountyId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounty_moderation_audit_HostId_OperationId",
                table: "bounty_moderation_audit",
                columns: new[] { "HostId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounty_pledges_HostId_BountyId",
                table: "bounty_pledges",
                columns: new[] { "HostId", "BountyId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounty_pledges_HostId_ContributorLogin_State",
                table: "bounty_pledges",
                columns: new[] { "HostId", "ContributorLogin", "State" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_bounty_pledges_HostId_OperationId",
                table: "bounty_pledges",
                columns: new[] { "HostId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_audits_CollectiveId_OperationId",
                table: "collective_audits",
                columns: new[] { "CollectiveId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_goal_host_totals_CollectiveGoalId_HostId",
                table: "collective_goal_host_totals",
                columns: new[] { "CollectiveGoalId", "HostId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_goal_host_totals_HostId_SourceBountyPublicId",
                table: "collective_goal_host_totals",
                columns: new[] { "HostId", "SourceBountyPublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_goals_CollectiveId",
                table: "collective_goals",
                column: "CollectiveId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_local_settings_CollectiveId_HostId",
                table: "collective_local_settings",
                columns: new[] { "CollectiveId", "HostId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_memberships_CollectiveId_HostId",
                table: "collective_memberships",
                columns: new[] { "CollectiveId", "HostId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_memberships_HostId",
                table: "collective_memberships",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_raid_handoffs_CollectiveRaidRelayId_OperationId",
                table: "collective_raid_handoffs",
                columns: new[] { "CollectiveRaidRelayId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_raid_relays_CollectiveId",
                table: "collective_raid_relays",
                column: "CollectiveId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_tournament_references_CollectiveId",
                table: "collective_tournament_references",
                column: "CollectiveId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_tournament_references_OwnerHostId_CompetitionPub~",
                table: "collective_tournament_references",
                columns: new[] { "OwnerHostId", "CompetitionPublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_collectives_CreationOperationId",
                table: "collectives",
                column: "CreationOperationId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collectives_PublicId",
                table: "collectives",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_command_aliases_HostId_Alias",
                table: "command_aliases",
                columns: new[] { "HostId", "Alias" },
                unique: true,
                filter: "\"GuessRoundProfileId\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_command_aliases_HostId_GuessRoundProfileId",
                table: "command_aliases",
                columns: new[] { "HostId", "GuessRoundProfileId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_command_aliases_HostId_GuessRoundProfileId_Alias",
                table: "command_aliases",
                columns: new[] { "HostId", "GuessRoundProfileId", "Alias" },
                unique: true,
                filter: "\"GuessRoundProfileId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_audits_DefinitionId",
                table: "community_audits",
                column: "DefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_audits_HostId_Action_OperationKey",
                table: "community_audits",
                columns: new[] { "HostId", "Action", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_audits_SeasonId",
                table: "community_audits",
                column: "SeasonId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_completions_DefinitionId",
                table: "community_completions",
                column: "DefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_completions_HostId_DefinitionId_SubjectKey_Sequen~",
                table: "community_completions",
                columns: new[] { "HostId", "DefinitionId", "SubjectKey", "Sequence" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_completions_HostId_PublicId",
                table: "community_completions",
                columns: new[] { "HostId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_definition_rewards_RewardDefinitionId",
                table: "community_definition_rewards",
                column: "RewardDefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_definitions_HostId_Key",
                table: "community_definitions",
                columns: new[] { "HostId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_definitions_HostId_PublicId",
                table: "community_definitions",
                columns: new[] { "HostId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_definitions_SeasonId",
                table: "community_definitions",
                column: "SeasonId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_equipped_rewards_HostId_ViewerTwitchUserId_Kind",
                table: "community_equipped_rewards",
                columns: new[] { "HostId", "ViewerTwitchUserId", "Kind" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_equipped_rewards_RewardDefinitionId",
                table: "community_equipped_rewards",
                column: "RewardDefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_events_HostId_Kind_OperationKey",
                table: "community_events",
                columns: new[] { "HostId", "Kind", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_events_SeasonId",
                table: "community_events",
                column: "SeasonId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_external_grant_receipts_HostId_CompletionId",
                table: "community_external_grant_receipts",
                columns: new[] { "HostId", "CompletionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_external_grant_receipts_HostId_Source_Idempotency~",
                table: "community_external_grant_receipts",
                columns: new[] { "HostId", "Source", "IdempotencyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_progress_DefinitionId",
                table: "community_progress",
                column: "DefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_progress_HostId_DefinitionId_SubjectKey",
                table: "community_progress",
                columns: new[] { "HostId", "DefinitionId", "SubjectKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reset_periods_DefinitionId",
                table: "community_reset_periods",
                column: "DefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reset_periods_HostId_DefinitionId_OperationKey",
                table: "community_reset_periods",
                columns: new[] { "HostId", "DefinitionId", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reset_periods_HostId_DefinitionId_PeriodKey",
                table: "community_reset_periods",
                columns: new[] { "HostId", "DefinitionId", "PeriodKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reward_definitions_HostId_Key",
                table: "community_reward_definitions",
                columns: new[] { "HostId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reward_definitions_HostId_PublicId",
                table: "community_reward_definitions",
                columns: new[] { "HostId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reward_definitions_SeasonId",
                table: "community_reward_definitions",
                column: "SeasonId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reward_unlocks_HostId_CompletionId",
                table: "community_reward_unlocks",
                columns: new[] { "HostId", "CompletionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reward_unlocks_HostId_RewardDefinitionId_ViewerTw~",
                table: "community_reward_unlocks",
                columns: new[] { "HostId", "RewardDefinitionId", "ViewerTwitchUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_reward_unlocks_RewardDefinitionId",
                table: "community_reward_unlocks",
                column: "RewardDefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_season_standings_HostId_SeasonId_ViewerTwitchUser~",
                table: "community_season_standings",
                columns: new[] { "HostId", "SeasonId", "ViewerTwitchUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_season_standings_SeasonId",
                table: "community_season_standings",
                column: "SeasonId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_seasons_HostId_CreationOperationId",
                table: "community_seasons",
                columns: new[] { "HostId", "CreationOperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_seasons_HostId_PublicId",
                table: "community_seasons",
                columns: new[] { "HostId", "PublicId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_seasons_HostId_Status",
                table: "community_seasons",
                columns: new[] { "HostId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_community_source_event_receipts_HostId_SourceKind_SourceEve~",
                table: "community_source_event_receipts",
                columns: new[] { "HostId", "SourceKind", "SourceEventId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_audits_HostId_CompetitionId",
                table: "competition_audits",
                columns: new[] { "HostId", "CompetitionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_audits_HostId_OperationId",
                table: "competition_audits",
                columns: new[] { "HostId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_entrant_members_HostId_CompetitionEntrantId_Log~",
                table: "competition_entrant_members",
                columns: new[] { "HostId", "CompetitionEntrantId", "Login" },
                unique: true,
                filter: "\"Login\" <> '[erased]'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_entrants_HostId_CompetitionId_Name",
                table: "competition_entrants",
                columns: new[] { "HostId", "CompetitionId", "Name" },
                unique: true,
                filter: "\"Name\" <> '[erased]'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_entrants_HostId_CompetitionId_RegistrationOpera~",
                table: "competition_entrants",
                columns: new[] { "HostId", "CompetitionId", "RegistrationOperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_entrants_PublicId",
                table: "competition_entrants",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_events_HostId_CompetitionId",
                table: "competition_events",
                columns: new[] { "HostId", "CompetitionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_events_HostId_OperationKey",
                table: "competition_events",
                columns: new[] { "HostId", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_matches_HostId_CompetitionId_Round_Position",
                table: "competition_matches",
                columns: new[] { "HostId", "CompetitionId", "Round", "Position" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_matches_HostId_EntrantAId",
                table: "competition_matches",
                columns: new[] { "HostId", "EntrantAId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_matches_HostId_EntrantBId",
                table: "competition_matches",
                columns: new[] { "HostId", "EntrantBId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_matches_HostId_WinnerEntrantId",
                table: "competition_matches",
                columns: new[] { "HostId", "WinnerEntrantId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_matches_PublicId",
                table: "competition_matches",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_matches_ReminderDueAtUtc_ReminderDeliveredAtUtc~",
                table: "competition_matches",
                columns: new[]
                {
                    "ReminderDueAtUtc",
                    "ReminderDeliveredAtUtc",
                    "ReminderSuppressedAtUtc",
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_milestone_reward_rules_HostId_CompetitionId_Win~",
                table: "competition_milestone_reward_rules",
                columns: new[] { "HostId", "CompetitionId", "WinsRequired" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_reward_receipts_HostId_CompetitionId_EntrantId_~",
                table: "competition_reward_receipts",
                columns: new[] { "HostId", "CompetitionId", "EntrantId", "Login", "RewardKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competitions_HostId_CreationOperationId",
                table: "competitions",
                columns: new[] { "HostId", "CreationOperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_competitions_HostId_Status",
                table: "competitions",
                columns: new[] { "HostId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_competitions_PublicId",
                table: "competitions",
                column: "PublicId",
                unique: true
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

            migrationBuilder.CreateIndex(
                name: "IX_custom_announcement_schedules_HostId_CustomAnnouncementId",
                table: "custom_announcement_schedules",
                columns: new[] { "HostId", "CustomAnnouncementId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_announcements_HostId_DeliveryPolicyId",
                table: "custom_announcements",
                columns: new[] { "HostId", "DeliveryPolicyId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_announcements_HostId_MessageLibraryEntryId",
                table: "custom_announcements",
                columns: new[] { "HostId", "MessageLibraryEntryId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_announcements_HostId_Name",
                table: "custom_announcements",
                columns: new[] { "HostId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_CounterId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "CounterId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_CustomCommandId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "CustomCommandId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_OneArgumentMessageLibraryEntr~",
                table: "custom_command_actions",
                columns: new[] { "HostId", "OneArgumentMessageLibraryEntryId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_TwoArgumentMessageLibraryEntr~",
                table: "custom_command_actions",
                columns: new[] { "HostId", "TwoArgumentMessageLibraryEntryId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_ZeroArgumentMessageLibraryEnt~",
                table: "custom_command_actions",
                columns: new[] { "HostId", "ZeroArgumentMessageLibraryEntryId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_aliases_CustomCommandId_SortOrder",
                table: "custom_command_aliases",
                columns: new[] { "CustomCommandId", "SortOrder" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_aliases_HostId_Alias",
                table: "custom_command_aliases",
                columns: new[] { "HostId", "Alias" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_aliases_HostId_CustomCommandId",
                table: "custom_command_aliases",
                columns: new[] { "HostId", "CustomCommandId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_invocation_claims_HostId_CustomCommandId_Tw~1",
                table: "custom_command_invocation_claims",
                columns: new[] { "HostId", "CustomCommandId", "TwitchUserId" },
                unique: true,
                filter: "\"TwitchUserId\" IS NOT NULL AND \"TwitchStreamId\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_invocation_claims_HostId_CustomCommandId_Tw~2",
                table: "custom_command_invocation_claims",
                columns: new[] { "HostId", "CustomCommandId", "TwitchUserId", "TwitchStreamId" },
                unique: true,
                filter: "\"TwitchUserId\" IS NOT NULL AND \"TwitchStreamId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_invocation_claims_HostId_CustomCommandId_Twi~",
                table: "custom_command_invocation_claims",
                columns: new[] { "HostId", "CustomCommandId", "TwitchStreamId" },
                unique: true,
                filter: "\"TwitchUserId\" IS NULL AND \"TwitchStreamId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_invocation_reset_audits_CustomCommandId",
                table: "custom_command_invocation_reset_audits",
                column: "CustomCommandId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_invocation_reset_audits_HostId_ResetAtUtc",
                table: "custom_command_invocation_reset_audits",
                columns: new[] { "HostId", "ResetAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_commands_HostId_Name",
                table: "custom_commands",
                columns: new[] { "HostId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_counters_HostId_Name",
                table: "custom_counters",
                columns: new[] { "HostId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_message_library_entries_HostId_Name",
                table: "custom_message_library_entries",
                columns: new[] { "HostId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_message_variants_CustomMessageLibraryEntryId_SortOrd~",
                table: "custom_message_variants",
                columns: new[] { "CustomMessageLibraryEntryId", "SortOrder" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_durable_alerts_HostId_AcknowledgedAtUtc",
                table: "durable_alerts",
                columns: new[] { "HostId", "AcknowledgedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_durable_alerts_HostId_Source_SourceKey",
                table: "durable_alerts",
                columns: new[] { "HostId", "Source", "SourceKey" },
                unique: true,
                filter: "\"AcknowledgedAtUtc\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_guess_options_GuessRoundProfileId_Name",
                table: "guess_options",
                columns: new[] { "GuessRoundProfileId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_guess_round_profiles_HostId",
                table: "guess_round_profiles",
                column: "HostId",
                unique: true,
                filter: "\"IsDefault\""
            );

            migrationBuilder.CreateIndex(
                name: "IX_guess_round_profiles_HostId_Slug",
                table: "guess_round_profiles",
                columns: new[] { "HostId", "Slug" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_guess_rounds_GuessRoundProfileId",
                table: "guess_rounds",
                column: "GuessRoundProfileId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_guess_rounds_HostId",
                table: "guess_rounds",
                column: "HostId",
                unique: true,
                filter: "\"Status\" IN ('Open', 'Closed')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_guess_votes_GuessRoundId_Login",
                table: "guess_votes",
                columns: new[] { "GuessRoundId", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_host_bot_account_settings_HostId",
                table: "host_bot_account_settings",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_host_broadcaster_authorizations_HostId",
                table: "host_broadcaster_authorizations",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_host_mod_access_entries_HostId_Kind_Login",
                table: "host_mod_access_entries",
                columns: new[] { "HostId", "Kind", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_host_mod_access_settings_HostId",
                table: "host_mod_access_settings",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_hosts_Login",
                table: "hosts",
                column: "Login",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_attachments_HostId_BountyId_MomentCandidateId",
                table: "moment_attachments",
                columns: new[] { "HostId", "BountyId", "MomentCandidateId" },
                unique: true,
                filter: "\"BountyId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_attachments_HostId_CommunityDefinitionId_MomentCandi~",
                table: "moment_attachments",
                columns: new[] { "HostId", "CommunityDefinitionId", "MomentCandidateId" },
                unique: true,
                filter: "\"CommunityDefinitionId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_attachments_HostId_CompetitionMatchId_MomentCandidat~",
                table: "moment_attachments",
                columns: new[] { "HostId", "CompetitionMatchId", "MomentCandidateId" },
                unique: true,
                filter: "\"CompetitionMatchId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_attachments_HostId_MomentCandidateId",
                table: "moment_attachments",
                columns: new[] { "HostId", "MomentCandidateId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_HostId_StreamIdentity_LastCapturedAtUtc",
                table: "moment_candidates",
                columns: new[] { "HostId", "StreamIdentity", "LastCapturedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_MergedIntoCandidateId",
                table: "moment_candidates",
                column: "MergedIntoCandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_PublicId",
                table: "moment_candidates",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_TwitchClipId",
                table: "moment_candidates",
                column: "TwitchClipId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_TwitchStreamMarkerId",
                table: "moment_candidates",
                column: "TwitchStreamMarkerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_capture_requests_CandidateId_CapturedAtUtc_Id",
                table: "moment_capture_requests",
                columns: new[] { "CandidateId", "CapturedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_contributors_CandidateId_FirstCapturedAtUtc_Id",
                table: "moment_contributors",
                columns: new[] { "CandidateId", "FirstCapturedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_contributors_CandidateId_IdentityKey",
                table: "moment_contributors",
                columns: new[] { "CandidateId", "IdentityKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_contributors_CandidateId_NormalizedLogin",
                table: "moment_contributors",
                columns: new[] { "CandidateId", "NormalizedLogin" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_events_CandidateId",
                table: "moment_events",
                column: "CandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_events_HostId_Id",
                table: "moment_events",
                columns: new[] { "HostId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_events_HostId_OperationKey",
                table: "moment_events",
                columns: new[] { "HostId", "OperationKey" },
                unique: true,
                filter: "\"OperationKey\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_hub_settings_HostId",
                table: "moment_hub_settings",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_merges_HostId_TargetCandidateId_MergedAtUtc",
                table: "moment_merges",
                columns: new[] { "HostId", "TargetCandidateId", "MergedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_merges_SourceCandidateId",
                table: "moment_merges",
                column: "SourceCandidateId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_merges_TargetCandidateId",
                table: "moment_merges",
                column: "TargetCandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_moderation_audit_CandidateId",
                table: "moment_moderation_audit",
                column: "CandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_moderation_audit_HostId_CandidateId_Id",
                table: "moment_moderation_audit",
                columns: new[] { "HostId", "CandidateId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_suggestions_CandidateId_CreatedAtUtc_Id",
                table: "moment_suggestions",
                columns: new[] { "CandidateId", "CreatedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_votes_CandidateId_IdentityKey",
                table: "moment_votes",
                columns: new[] { "CandidateId", "IdentityKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_votes_CandidateId_NormalizedLogin",
                table: "moment_votes",
                columns: new[] { "CandidateId", "NormalizedLogin" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_weekly_finalizations_HostId_WeekStartsAtUtc",
                table: "moment_weekly_finalizations",
                columns: new[] { "HostId", "WeekStartsAtUtc" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_weekly_finalizations_WinningCandidateId",
                table: "moment_weekly_finalizations",
                column: "WinningCandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cue_media_asset_references_AssetId_HostId",
                table: "overlay_cue_media_asset_references",
                columns: new[] { "AssetId", "HostId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cue_media_asset_references_CueId_HostId",
                table: "overlay_cue_media_asset_references",
                columns: new[] { "CueId", "HostId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cue_media_asset_references_HostId_AssetId",
                table: "overlay_cue_media_asset_references",
                columns: new[] { "HostId", "AssetId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cues_HostId_Name_PublicId",
                table: "overlay_cues",
                columns: new[] { "HostId", "Name", "PublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cues_PublicId",
                table: "overlay_cues",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_event_feed_items_HostId_OverlayInstanceId_Lifecycle~",
                table: "overlay_event_feed_items",
                columns: new[] { "HostId", "OverlayInstanceId", "Lifecycle", "EnqueuedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_event_feed_items_OverlayInstanceId_Kind_SourceKey",
                table: "overlay_event_feed_items",
                columns: new[] { "OverlayInstanceId", "Kind", "SourceKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_instance_events_HostId_OverlayPublicId_Id",
                table: "overlay_instance_events",
                columns: new[] { "HostId", "OverlayPublicId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_instances_AccessKeyDigest",
                table: "overlay_instances",
                column: "AccessKeyDigest",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_instances_HostId_UpdatedAtUtc_PublicId",
                table: "overlay_instances",
                columns: new[] { "HostId", "UpdatedAtUtc", "PublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_instances_PublicId",
                table: "overlay_instances",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_assets_DocumentId",
                table: "overlay_media_assets",
                column: "DocumentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_assets_HostId_Name_PublicId",
                table: "overlay_media_assets",
                columns: new[] { "HostId", "Name", "PublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_assets_PublicId",
                table: "overlay_media_assets",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_documents_State_UpdatedAtUtc",
                table: "overlay_media_documents",
                columns: new[] { "State", "UpdatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_documents_StorageKey",
                table: "overlay_media_documents",
                column: "StorageKey",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_entries_QueueId_IdentityKey",
                table: "play_queue_entries",
                columns: new[] { "QueueId", "IdentityKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_entries_QueueId_NormalizedLogin",
                table: "play_queue_entries",
                columns: new[] { "QueueId", "NormalizedLogin" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_entries_QueueId_Status_Priority_JoinedAtUtc_Id",
                table: "play_queue_entries",
                columns: new[] { "QueueId", "Status", "Priority", "JoinedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_entry_values_EntryId_FieldId",
                table: "play_queue_entry_values",
                columns: new[] { "EntryId", "FieldId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_entry_values_FieldId",
                table: "play_queue_entry_values",
                column: "FieldId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_events_HostId_Id",
                table: "play_queue_events",
                columns: new[] { "HostId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_events_QueueId",
                table: "play_queue_events",
                column: "QueueId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_exclusions_HostId",
                table: "play_queue_exclusions",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_exclusions_QueueId_IdentityKey_ExpiresAtUtc",
                table: "play_queue_exclusions",
                columns: new[] { "QueueId", "IdentityKey", "ExpiresAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_fields_QueueId_Key",
                table: "play_queue_fields",
                columns: new[] { "QueueId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_fields_QueueId_Position",
                table: "play_queue_fields",
                columns: new[] { "QueueId", "Position" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_participation_HostId",
                table: "play_queue_participation",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_participation_QueueId_IdentityKey_ParticipatedAt~",
                table: "play_queue_participation",
                columns: new[] { "QueueId", "IdentityKey", "ParticipatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queue_role_requirements_QueueId_Role",
                table: "play_queue_role_requirements",
                columns: new[] { "QueueId", "Role" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_play_queues_HostId_Slug",
                table: "play_queues",
                columns: new[] { "HostId", "Slug" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_plugin_automation_instantiations_EnableOperationId_Template~",
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
                name: "IX_plugin_automation_instantiations_PluginId_FeatureId_HostId_~",
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

            migrationBuilder.CreateIndex(
                name: "IX_point_balances_HostId_Login",
                table: "point_balances",
                columns: new[] { "HostId", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_HostId_BountyPledgeId",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "BountyPledgeId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_HostId_BountyRewardId",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "BountyRewardId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_HostId_CommunityCompletionId",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "CommunityCompletionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_HostId_CreatedAtUtc",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_HostId_OperationKey",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "OperationKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_RequestSubmissionId",
                table: "point_ledger_entries",
                column: "RequestSubmissionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_points_giveaway_entrants_GiveawayId_Login",
                table: "points_giveaway_entrants",
                columns: new[] { "GiveawayId", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_points_giveaway_winners_GiveawayId",
                table: "points_giveaway_winners",
                column: "GiveawayId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_points_giveaways_HostId",
                table: "points_giveaways",
                column: "HostId",
                unique: true,
                filter: "\"Status\" = 'Active'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_points_settings_HostId",
                table: "points_settings",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_outbox_ClaimSlot",
                table: "public_chat_outbox",
                column: "ClaimSlot",
                unique: true,
                filter: "\"ClaimSlot\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_outbox_ClaimToken",
                table: "public_chat_outbox",
                column: "ClaimToken",
                unique: true,
                filter: "\"ClaimToken\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_outbox_Status_ClaimExpiresAtUtc",
                table: "public_chat_outbox",
                columns: new[] { "Status", "ClaimExpiresAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_outbox_Status_ExpiresAtUtc",
                table: "public_chat_outbox",
                columns: new[] { "Status", "ExpiresAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_outbox_Status_NextAttemptAtUtc_CreatedAtUtc_Id",
                table: "public_chat_outbox",
                columns: new[] { "Status", "NextAttemptAtUtc", "CreatedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_pin_operations_HostId",
                table: "public_chat_pin_operations",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_pin_operations_OutboxMessageId",
                table: "public_chat_pin_operations",
                column: "OutboxMessageId",
                unique: true,
                filter: "\"OutboxMessageId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_pin_operations_Status_CreatedAtUtc_Id",
                table: "public_chat_pin_operations",
                columns: new[] { "Status", "CreatedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_send_receipts_AttemptedAtUtc",
                table: "public_chat_send_receipts",
                column: "AttemptedAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_public_chat_send_receipts_DeliveredAtUtc",
                table: "public_chat_send_receipts",
                column: "DeliveredAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_raid_collaboration_history_HostId_OccurredAtUtc",
                table: "raid_collaboration_history",
                columns: new[] { "HostId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_raid_collaboration_history_HostId_ProviderMessageId",
                table: "raid_collaboration_history",
                columns: new[] { "HostId", "ProviderMessageId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_raid_collaboration_settings_HostId",
                table: "raid_collaboration_settings",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_reply_delivery_settings_HostId_Feature_ScopeId_ReplyKey",
                table: "reply_delivery_settings",
                columns: new[] { "HostId", "Feature", "ScopeId", "ReplyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_reply_pin_policies_HostId_Feature_ReplyKey",
                table: "reply_pin_policies",
                columns: new[] { "HostId", "Feature", "ReplyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_reply_settings_GuessRoundProfileId",
                table: "reply_settings",
                column: "GuessRoundProfileId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_board_events_BoardId",
                table: "request_board_events",
                column: "BoardId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_board_events_HostId_Id",
                table: "request_board_events",
                columns: new[] { "HostId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_board_fields_BoardId_Key",
                table: "request_board_fields",
                columns: new[] { "BoardId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_board_fields_BoardId_Position",
                table: "request_board_fields",
                columns: new[] { "BoardId", "Position" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_boards_HostId_Slug",
                table: "request_boards",
                columns: new[] { "HostId", "Slug" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submission_values_FieldId",
                table: "request_submission_values",
                column: "FieldId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submission_values_SubmissionId_FieldId",
                table: "request_submission_values",
                columns: new[] { "SubmissionId", "FieldId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submission_votes_SubmissionId_VoterLogin",
                table: "request_submission_votes",
                columns: new[] { "SubmissionId", "VoterLogin" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_BoardId_NormalizedTitle",
                table: "request_submissions",
                columns: new[] { "BoardId", "NormalizedTitle" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_BoardId_NormalizedUrl",
                table: "request_submissions",
                columns: new[] { "BoardId", "NormalizedUrl" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_BoardId_Status_Priority_QueuePosition",
                table: "request_submissions",
                columns: new[] { "BoardId", "Status", "Priority", "QueuePosition" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_HostId_OperationId",
                table: "request_submissions",
                columns: new[] { "HostId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_HostId_SubmitterLogin_PointReservationS~",
                table: "request_submissions",
                columns: new[] { "HostId", "SubmitterLogin", "PointReservationState" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_MergedIntoSubmissionId",
                table: "request_submissions",
                column: "MergedIntoSubmissionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_shoutout_cooldowns_HostId_TargetTwitchUserId",
                table: "shoutout_cooldowns",
                columns: new[] { "HostId", "TargetTwitchUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_shoutout_history_HostId_OccurredAtUtc",
                table: "shoutout_history",
                columns: new[] { "HostId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_shoutout_history_HostId_ProviderMessageId",
                table: "shoutout_history",
                columns: new[] { "HostId", "ProviderMessageId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_site_access_entries_Kind_Login",
                table: "site_access_entries",
                columns: new[] { "Kind", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_clips_HostId_IdempotencyKey",
                table: "twitch_clips",
                columns: new[] { "HostId", "IdempotencyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_clips_HostId_Status_ResolvedAtUtc",
                table: "twitch_clips",
                columns: new[] { "HostId", "Status", "ResolvedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_custom_rewards_HostId_ProviderRewardId",
                table: "twitch_custom_rewards",
                columns: new[] { "HostId", "ProviderRewardId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_poll_template_choices_TwitchPollTemplateId_Position",
                table: "twitch_poll_template_choices",
                columns: new[] { "TwitchPollTemplateId", "Position" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_poll_templates_HostId",
                table: "twitch_poll_templates",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_polls_HostId",
                table: "twitch_polls",
                column: "HostId",
                unique: true,
                filter: "\"Status\" = 'Active'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_polls_HostId_EndedAtUtc",
                table: "twitch_polls",
                columns: new[] { "HostId", "EndedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_polls_HostId_ProviderPollId",
                table: "twitch_polls",
                columns: new[] { "HostId", "ProviderPollId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_prediction_template_outcomes_TwitchPredictionTemplat~",
                table: "twitch_prediction_template_outcomes",
                columns: new[] { "TwitchPredictionTemplateId", "Position" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_prediction_templates_HostId",
                table: "twitch_prediction_templates",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_predictions_HostId",
                table: "twitch_predictions",
                column: "HostId",
                unique: true,
                filter: "\"Status\" IN ('Active', 'Locked')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_predictions_HostId_EndedAtUtc",
                table: "twitch_predictions",
                columns: new[] { "HostId", "EndedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_predictions_HostId_ProviderPredictionId",
                table: "twitch_predictions",
                columns: new[] { "HostId", "ProviderPredictionId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_reward_redemptions_HostId_ProviderRedemptionId",
                table: "twitch_reward_redemptions",
                columns: new[] { "HostId", "ProviderRedemptionId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_reward_redemptions_HostId_Status_UpdatedAtUtc",
                table: "twitch_reward_redemptions",
                columns: new[] { "HostId", "Status", "UpdatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_stream_markers_HostId_CreatedAtUtc",
                table: "twitch_stream_markers",
                columns: new[] { "HostId", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_twitch_stream_markers_HostId_IdempotencyKey",
                table: "twitch_stream_markers",
                columns: new[] { "HostId", "IdempotencyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_ambiguous_logins_HostId_Login",
                table: "viewer_passport_ambiguous_logins",
                columns: new[] { "HostId", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_logins_HostId_Login",
                table: "viewer_passport_logins",
                columns: new[] { "HostId", "Login" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_logins_HostId_PassportId_Login",
                table: "viewer_passport_logins",
                columns: new[] { "HostId", "PassportId", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_logins_PassportId",
                table: "viewer_passport_logins",
                column: "PassportId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_stream_attendance_HostId_PassportId_StreamS~",
                table: "viewer_passport_stream_attendance",
                columns: new[] { "HostId", "PassportId", "StreamSessionId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_stream_attendance_HostId_StreamSessionId",
                table: "viewer_passport_stream_attendance",
                columns: new[] { "HostId", "StreamSessionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_stream_sessions_HostId_ContinuityGeneration~",
                table: "viewer_passport_stream_sessions",
                columns: new[]
                {
                    "HostId",
                    "ContinuityGeneration",
                    "StartedAtUtc",
                    "TwitchStreamId",
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_stream_sessions_HostId_TwitchStreamId",
                table: "viewer_passport_stream_sessions",
                columns: new[] { "HostId", "TwitchStreamId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passports_HostId_Login",
                table: "viewer_passports",
                columns: new[] { "HostId", "Login" },
                unique: true,
                filter: "\"Login\" <> ''"
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passports_HostId_TwitchUserId",
                table: "viewer_passports",
                columns: new[] { "HostId", "TwitchUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passports_SelectedBadgeRewardDefinitionId",
                table: "viewer_passports",
                column: "SelectedBadgeRewardDefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passports_SelectedTitleRewardDefinitionId",
                table: "viewer_passports",
                column: "SelectedTitleRewardDefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_whisper_quota_buckets_HostId_BotTwitchUserId_DayUtc",
                table: "whisper_quota_buckets",
                columns: new[] { "HostId", "BotTwitchUserId", "DayUtc" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_whisper_quota_recipients_WhisperQuotaBucketId_RecipientTwit~",
                table: "whisper_quota_recipients",
                columns: new[] { "WhisperQuotaBucketId", "RecipientTwitchUserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("PostgreSql database downgrade is not supported.");
        }
    }
}
