using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _20260722_InitialHetznerBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hosts",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ProfileImageUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    ChannelBotAuthorizedAtUtc = table.Column<DateTime>(
                        type: "TEXT",
                        nullable: true
                    ),
                    ChannelBotAuthorizedScopes = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    BotRuntimeState = table.Column<int>(type: "INTEGER", nullable: false),
                    BotRuntimeStateChangedAtUtc = table.Column<DateTime>(
                        type: "TEXT",
                        nullable: true
                    ),
                    EnabledFeatures = table.Column<long>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 7L
                    ),
                    TimeZoneId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false,
                        defaultValue: "UTC"
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hosts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "public_chat_outbox",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    DeduplicationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SafePreSendFailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClaimSlot = table.Column<int>(type: "INTEGER", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SendStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailurePhase = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: true
                    ),
                    FailureType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    RejectionCode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_chat_outbox", x => x.Id);
                    table.CheckConstraint(
                        "CK_public_chat_outbox_AttemptCount",
                        "AttemptCount >= 0"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_Channel",
                        "length(trim(Channel)) > 0"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_DeduplicationKey",
                        "DeduplicationKey IS NULL OR length(DeduplicationKey) = 64"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_FailurePhase",
                        "FailurePhase IS NULL OR FailurePhase IN ('Preparation', 'Send')"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_SafePreSendFailureCount",
                        "SafePreSendFailureCount >= 0"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_State",
                        "(Status = 'Pending' AND length(Message) > 0 AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL AND AttemptCount = 0 AND SafePreSendFailureCount = 0 AND length(DeduplicationKey) = 64 AND NextAttemptAtUtc IS NOT NULL AND FailurePhase IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR (Status = 'Claimed' AND length(Message) > 0 AND ClaimToken IS NOT NULL AND ClaimSlot = 1 AND ClaimExpiresAtUtc IS NOT NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL AND AttemptCount = 0 AND length(DeduplicationKey) = 64 AND NextAttemptAtUtc IS NOT NULL AND ((SafePreSendFailureCount = 0 AND FailurePhase IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR (SafePreSendFailureCount > 0 AND FailurePhase = 'Preparation' AND length(FailureType) > 0 AND RejectionCode IS NULL))) OR (Status = 'Sending' AND length(Message) > 0 AND ClaimToken IS NOT NULL AND ClaimSlot = 1 AND ClaimExpiresAtUtc IS NOT NULL AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NULL AND AttemptCount > 0 AND length(DeduplicationKey) = 64 AND NextAttemptAtUtc IS NOT NULL AND FailurePhase IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR (Status = 'SafePreSendTransient' AND length(Message) > 0 AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL AND AttemptCount = 0 AND length(DeduplicationKey) = 64 AND NextAttemptAtUtc IS NOT NULL AND SafePreSendFailureCount > 0 AND FailurePhase = 'Preparation' AND length(FailureType) > 0 AND RejectionCode IS NULL) OR (Status = 'SafePreSendExhausted' AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 AND SafePreSendFailureCount > 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND FailurePhase = 'Preparation' AND length(FailureType) > 0 AND RejectionCode IS NULL) OR (Status IN ('MissingChannel', 'MissingBot') AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 AND SafePreSendFailureCount = 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND FailurePhase = 'Preparation' AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL) OR (Status = 'Rejected' AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NOT NULL AND FailurePhase = 'Send' AND AttemptCount > 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND (RejectionCode IS NULL OR length(RejectionCode) > 0)) OR (Status = 'Ambiguous' AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NOT NULL AND FailurePhase = 'Send' AND AttemptCount > 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND length(FailureType) > 0 AND RejectionCode IS NULL) OR (Status = 'Unexpected' AND Message IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NOT NULL AND AttemptCount = 0 AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND FailurePhase = 'Preparation' AND length(FailureType) > 0 AND RejectionCode IS NULL) OR (Status = 'Expired' AND Message IS NULL AND DeduplicationKey IS NULL AND NextAttemptAtUtc IS NULL AND ClaimToken IS NULL AND ClaimSlot IS NULL AND ClaimExpiresAtUtc IS NULL AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NOT NULL AND FailurePhase IS NULL AND FailureType IS NULL AND HttpStatusCode IS NULL AND RejectionCode IS NULL)"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_outbox_Status",
                        "Status IN ('Ambiguous', 'Claimed', 'Expired', 'MissingBot', 'MissingChannel', 'Pending', 'Rejected', 'SafePreSendExhausted', 'SafePreSendTransient', 'Sending', 'Unexpected')"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "public_chat_send_receipts",
                columns: table => new
                {
                    OutboxMessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeliveredDeduplicationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    DeliveredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_chat_send_receipts", x => x.OutboxMessageId);
                    table.CheckConstraint(
                        "CK_public_chat_send_receipts_Delivery",
                        "(DeliveredDeduplicationKey IS NULL AND DeliveredAtUtc IS NULL) OR (length(DeliveredDeduplicationKey) = 64 AND DeliveredAtUtc IS NOT NULL)"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "site_access_entries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_access_entries", x => x.Id);
                    table.CheckConstraint(
                        "CK_site_access_entries_Kind",
                        "Kind IN ('blacklist', 'whitelist')"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "site_access_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WhitelistEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_access_settings", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_announcement_delivery_policies",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PolicyType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    RetryDelayTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    OccurrenceLifetimeTicks = table.Column<long>(type: "INTEGER", nullable: true),
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
                        "PolicyType = 'RetryUntilExpiredThenSkip' AND RetryDelayTicks IS NOT NULL AND RetryDelayTicks > 0 AND OccurrenceLifetimeTicks IS NOT NULL AND OccurrenceLifetimeTicks <= 600000000 AND RetryDelayTicks < OccurrenceLifetimeTicks"
                    );
                    table.CheckConstraint(
                        "CK_custom_announcement_delivery_policies_PolicyType",
                        "PolicyType IN ('RetryUntilExpiredThenSkip')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ModeratorOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CooldownScope = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        "CooldownScope IN ('Global', 'User')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SelectionMode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    CurrentVariantIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        "SelectionMode IN ('First', 'Random', 'Sequential')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    LinkPath = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcknowledgedByLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_durable_alerts", x => x.Id);
                    table.CheckConstraint(
                        "CK_durable_alerts_Severity",
                        "Severity IN ('Critical', 'Info', 'Warning')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 0L
                    ),
                    WinningGuessPointReward = table.Column<string>(
                        type: "TEXT",
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    OverrideEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    WhisperResponsesEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ProfileImageUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    ProtectedTokenPayload = table.Column<byte[]>(type: "BLOB", nullable: true),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AuthorizedScopes = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "host_mod_access_entries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_mod_access_entries", x => x.Id);
                    table.CheckConstraint(
                        "CK_host_mod_access_entries_Kind",
                        "Kind IN ('blacklist', 'whitelist')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ModsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowModsByDefault = table.Column<bool>(
                        type: "INTEGER",
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
                name: "point_balances",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Amount = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "point_ledger_entries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Delta = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BalanceAfter = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CounterpartyLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    GiveawayId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_ledger_entries", x => x.Id);
                    table.CheckConstraint(
                        "CK_point_ledger_entries_Kind",
                        "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin')"
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
                name: "points_giveaways",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MinimumPayout = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    MaximumPayout = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    WinnerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Eligibility = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_giveaways", x => x.Id);
                    table.CheckConstraint(
                        "CK_points_giveaways_Eligibility",
                        "Eligibility IN ('everyone', 'followers', 'subscribers')"
                    );
                    table.CheckConstraint(
                        "CK_points_giveaways_Status",
                        "Status IN ('Active', 'Cancelled', 'Completed', 'Expired')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PointLabel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GamblingWinRatePercent = table.Column<int>(type: "INTEGER", nullable: false),
                    GamblingCooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    GiveawayDurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    GiveawayMinimumPayout = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    GiveawayMaximumPayout = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    GiveawayWinnerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GiveawayEligibility = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    GiveawayCooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    BalanceReply = table.Column<string>(type: "TEXT", nullable: false),
                    OtherBalanceReply = table.Column<string>(type: "TEXT", nullable: false),
                    TransferReply = table.Column<string>(type: "TEXT", nullable: false),
                    AddReply = table.Column<string>(type: "TEXT", nullable: false),
                    RemoveReply = table.Column<string>(type: "TEXT", nullable: false),
                    InvalidAmountReply = table.Column<string>(type: "TEXT", nullable: false),
                    InsufficientBalanceReply = table.Column<string>(type: "TEXT", nullable: false),
                    ModeratorOnlyReply = table.Column<string>(type: "TEXT", nullable: false),
                    GamblingWinReply = table.Column<string>(type: "TEXT", nullable: false),
                    GamblingLoseReply = table.Column<string>(type: "TEXT", nullable: false),
                    GiveawayStartedReply = table.Column<string>(type: "TEXT", nullable: false),
                    GiveawayUpdateReply = table.Column<string>(type: "TEXT", nullable: false),
                    GiveawayJoinedReply = table.Column<string>(type: "TEXT", nullable: false),
                    GiveawayAlreadyJoinedReply = table.Column<string>(
                        type: "TEXT",
                        nullable: false
                    ),
                    GiveawayEndedReply = table.Column<string>(type: "TEXT", nullable: false),
                    GiveawayNoEntrantsReply = table.Column<string>(type: "TEXT", nullable: false),
                    GiveawayCancelledReply = table.Column<string>(type: "TEXT", nullable: false),
                    GiveawayAlreadyActiveReply = table.Column<string>(
                        type: "TEXT",
                        nullable: false
                    ),
                    GiveawayNotActiveReply = table.Column<string>(type: "TEXT", nullable: false),
                    GiveawayCooldownReply = table.Column<string>(type: "TEXT", nullable: false),
                    StreamOfflineReply = table.Column<string>(type: "TEXT", nullable: false),
                    NotEligibleReply = table.Column<string>(type: "TEXT", nullable: false),
                    FollowerChecksUnavailableReply = table.Column<string>(
                        type: "TEXT",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points_settings", x => x.Id);
                    table.CheckConstraint(
                        "CK_points_settings_GiveawayEligibility",
                        "GiveawayEligibility IN ('everyone', 'followers', 'subscribers')"
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
                name: "reply_delivery_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reply_delivery_settings", x => x.Id);
                    table.CheckConstraint(
                        "CK_reply_delivery_settings_Feature",
                        "Feature IN ('guessing', 'points')"
                    );
                    table.CheckConstraint(
                        "CK_reply_delivery_settings_Target",
                        "Target IN ('chat', 'whisper')"
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
                name: "whisper_quota_buckets",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    BotTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    DayUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Exhausted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "custom_command_aliases",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomCommandId = table.Column<int>(type: "INTEGER", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_command_aliases_custom_commands_HostId_CustomCommandId",
                        columns: x => new { x.HostId, x.CustomCommandId },
                        principalTable: "custom_commands",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_announcements",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MessageLibraryEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeliveryPolicyId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeliveryType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false,
                        defaultValue: "ChatMessage"
                    ),
                    AnnouncementColor = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "Primary"
                    ),
                    LatestDeliveryResult = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "None"
                    ),
                    LastSentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastOccurrenceAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OccurrenceStatus = table.Column<string>(
                        type: "TEXT",
                        maxLength: 40,
                        nullable: false
                    ),
                    OccurrenceDueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OccurrenceExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OccurrenceNextAttemptAtUtc = table.Column<DateTime>(
                        type: "TEXT",
                        nullable: true
                    ),
                    OccurrenceCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OccurrenceAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurrenceMessage = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    ChatMessagesSinceLastSent = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_announcements", x => x.Id);
                    table.UniqueConstraint(
                        "AK_custom_announcements_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint(
                        "CK_custom_announcements_OccurrenceState",
                        "(OccurrenceStatus = 'None' AND OccurrenceDueAtUtc IS NULL AND OccurrenceExpiresAtUtc IS NULL AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'Pending' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NOT NULL AND OccurrenceNextAttemptAtUtc <= OccurrenceExpiresAtUtc AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'Attempting' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount > 0 AND length(OccurrenceMessage) > 0) OR (OccurrenceStatus = 'RetryScheduled' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc >= OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc <= OccurrenceExpiresAtUtc AND OccurrenceCompletedAtUtc IS NULL AND OccurrenceAttemptCount > 0 AND length(OccurrenceMessage) > 0) OR (OccurrenceStatus IN ('Accepted', 'TerminalRejected', 'TerminalAmbiguous', 'TerminalUnexpected') AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount > 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'SkippedExpired' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount >= 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'TerminalMissingMessage' AND OccurrenceDueAtUtc IS NOT NULL AND OccurrenceExpiresAtUtc > OccurrenceDueAtUtc AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL) OR (OccurrenceStatus = 'TerminalInvalidTimeZone' AND OccurrenceDueAtUtc IS NULL AND OccurrenceExpiresAtUtc IS NULL AND OccurrenceNextAttemptAtUtc IS NULL AND OccurrenceCompletedAtUtc IS NOT NULL AND OccurrenceAttemptCount = 0 AND OccurrenceMessage IS NULL)"
                    );
                    table.CheckConstraint(
                        "CK_custom_announcements_OccurrenceStatus",
                        "OccurrenceStatus IN ('Accepted', 'Attempting', 'None', 'Pending', 'RetryScheduled', 'SkippedExpired', 'TerminalAmbiguous', 'TerminalInvalidTimeZone', 'TerminalMissingMessage', 'TerminalRejected', 'TerminalUnexpected')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_announcements_custom_announcement_delivery_policies_HostId_DeliveryPolicyId",
                        columns: x => new { x.HostId, x.DeliveryPolicyId },
                        principalTable: "custom_announcement_delivery_policies",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_custom_announcements_custom_message_library_entries_HostId_MessageLibraryEntryId",
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
                    CustomCommandId = table.Column<int>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    MessageLibraryEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CounterId = table.Column<int>(type: "INTEGER", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_actions", x => x.CustomCommandId);
                    table.CheckConstraint(
                        "CK_custom_command_actions_ActionType",
                        "ActionType IN ('Counter', 'Message')"
                    );
                    table.CheckConstraint(
                        "CK_custom_command_actions_Payload",
                        "(ActionType = 'Message' AND CounterId IS NULL) OR (ActionType = 'Counter' AND CounterId IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_actions_custom_commands_HostId_CustomCommandId",
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
                        name: "FK_custom_command_actions_custom_message_library_entries_HostId_MessageLibraryEntryId",
                        columns: x => new { x.HostId, x.MessageLibraryEntryId },
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomMessageLibraryEntryId = table.Column<int>(
                        type: "INTEGER",
                        nullable: false
                    ),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_message_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_message_variants_custom_message_library_entries_CustomMessageLibraryEntryId",
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GuessRoundProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_aliases", x => x.Id);
                    table.CheckConstraint(
                        "CK_command_aliases_Kind",
                        "Kind IN ('AddPoints', 'CancelGiveaway', 'EndGiveaway', 'Gamble', 'Giveaway', 'GivePoints', 'Guess', 'Guesses', 'Join', 'Points', 'RemovePoints', 'Start', 'Stop', 'Win')"
                    );
                    table.ForeignKey(
                        name: "FK_command_aliases_guess_round_profiles_HostId_GuessRoundProfileId",
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuessRoundProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReplyText = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 0
                    ),
                    ReplyTarget = table.Column<string>(
                        type: "TEXT",
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
                        "ReplyTarget IN ('chat', 'whisper')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GuessRoundProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WinningName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guess_rounds", x => x.Id);
                    table.CheckConstraint(
                        "CK_guess_rounds_Status",
                        "Status IN ('Closed', 'Completed', 'Open')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuessRoundProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundStartedReply = table.Column<string>(type: "TEXT", nullable: false),
                    RoundAlreadyOpenReply = table.Column<string>(type: "TEXT", nullable: false),
                    NoOpenRoundReply = table.Column<string>(type: "TEXT", nullable: false),
                    GuessingStoppedReply = table.Column<string>(type: "TEXT", nullable: false),
                    GuessingAlreadyStoppedReply = table.Column<string>(
                        type: "TEXT",
                        nullable: false
                    ),
                    GuessingClosedReply = table.Column<string>(type: "TEXT", nullable: false),
                    InvalidGuessReply = table.Column<string>(type: "TEXT", nullable: false),
                    GuessUsageReply = table.Column<string>(type: "TEXT", nullable: false),
                    AvailableGuessesReply = table.Column<string>(type: "TEXT", nullable: false),
                    WinUsageReply = table.Column<string>(type: "TEXT", nullable: false),
                    ModeratorOnlyReply = table.Column<string>(type: "TEXT", nullable: false),
                    WinnerReply = table.Column<string>(type: "TEXT", nullable: false),
                    NoWinnersReply = table.Column<string>(type: "TEXT", nullable: false),
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
                name: "points_giveaway_entrants",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GiveawayId = table.Column<int>(type: "INTEGER", nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GiveawayId = table.Column<int>(type: "INTEGER", nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Payout = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
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
                name: "whisper_quota_recipients",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WhisperQuotaBucketId = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipientTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    RecipientLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    FirstSentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whisper_quota_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_whisper_quota_recipients_whisper_quota_buckets_WhisperQuotaBucketId",
                        column: x => x.WhisperQuotaBucketId,
                        principalTable: "whisper_quota_buckets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_announcement_schedules",
                columns: table => new
                {
                    CustomAnnouncementId = table.Column<int>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduleType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    IntervalMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    RequiredChatMessages = table.Column<int>(type: "INTEGER", nullable: true),
                    WeeklyDay = table.Column<int>(type: "INTEGER", nullable: true),
                    WeeklyTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_custom_announcement_schedules",
                        x => x.CustomAnnouncementId
                    );
                    table.CheckConstraint(
                        "CK_custom_announcement_schedules_Payload",
                        "(ScheduleType = 'Interval' AND IntervalMinutes >= 1 AND RequiredChatMessages IS NULL AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR (ScheduleType = 'IntervalAfterChat' AND IntervalMinutes >= 1 AND RequiredChatMessages >= 1 AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR (ScheduleType = 'Weekly' AND IntervalMinutes IS NULL AND RequiredChatMessages IS NULL AND WeeklyDay BETWEEN 0 AND 6 AND WeeklyTime IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_custom_announcement_schedules_ScheduleType",
                        "ScheduleType IN ('Interval', 'IntervalAfterChat', 'Weekly')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_announcement_schedules_custom_announcements_HostId_CustomAnnouncementId",
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuessRoundId = table.Column<int>(type: "INTEGER", nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GuessName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GuessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_command_aliases_HostId_Alias",
                table: "command_aliases",
                columns: new[] { "HostId", "Alias" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_command_aliases_HostId_GuessRoundProfileId",
                table: "command_aliases",
                columns: new[] { "HostId", "GuessRoundProfileId" }
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
                name: "IX_custom_command_actions_HostId_MessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "MessageLibraryEntryId" }
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
                name: "IX_custom_message_variants_CustomMessageLibraryEntryId_SortOrder",
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
                filter: "\"IsDefault\" = 1"
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
                name: "IX_point_balances_HostId_Login",
                table: "point_balances",
                columns: new[] { "HostId", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_HostId_CreatedAtUtc",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "CreatedAtUtc" }
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
                name: "IX_reply_delivery_settings_HostId_Feature_ScopeId_ReplyKey",
                table: "reply_delivery_settings",
                columns: new[] { "HostId", "Feature", "ScopeId", "ReplyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_reply_settings_GuessRoundProfileId",
                table: "reply_settings",
                column: "GuessRoundProfileId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_site_access_entries_Kind_Login",
                table: "site_access_entries",
                columns: new[] { "Kind", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_whisper_quota_buckets_HostId_BotTwitchUserId_DayUtc",
                table: "whisper_quota_buckets",
                columns: new[] { "HostId", "BotTwitchUserId", "DayUtc" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_whisper_quota_recipients_WhisperQuotaBucketId_RecipientTwitchUserId",
                table: "whisper_quota_recipients",
                columns: new[] { "WhisperQuotaBucketId", "RecipientTwitchUserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "command_aliases");

            migrationBuilder.DropTable(name: "custom_announcement_schedules");

            migrationBuilder.DropTable(name: "custom_command_actions");

            migrationBuilder.DropTable(name: "custom_command_aliases");

            migrationBuilder.DropTable(name: "custom_message_variants");

            migrationBuilder.DropTable(name: "durable_alerts");

            migrationBuilder.DropTable(name: "guess_options");

            migrationBuilder.DropTable(name: "guess_votes");

            migrationBuilder.DropTable(name: "host_bot_account_settings");

            migrationBuilder.DropTable(name: "host_mod_access_entries");

            migrationBuilder.DropTable(name: "host_mod_access_settings");

            migrationBuilder.DropTable(name: "point_balances");

            migrationBuilder.DropTable(name: "point_ledger_entries");

            migrationBuilder.DropTable(name: "points_giveaway_entrants");

            migrationBuilder.DropTable(name: "points_giveaway_winners");

            migrationBuilder.DropTable(name: "points_settings");

            migrationBuilder.DropTable(name: "public_chat_outbox");

            migrationBuilder.DropTable(name: "public_chat_send_receipts");

            migrationBuilder.DropTable(name: "reply_delivery_settings");

            migrationBuilder.DropTable(name: "reply_settings");

            migrationBuilder.DropTable(name: "site_access_entries");

            migrationBuilder.DropTable(name: "site_access_settings");

            migrationBuilder.DropTable(name: "whisper_quota_recipients");

            migrationBuilder.DropTable(name: "custom_announcements");

            migrationBuilder.DropTable(name: "custom_counters");

            migrationBuilder.DropTable(name: "custom_commands");

            migrationBuilder.DropTable(name: "guess_rounds");

            migrationBuilder.DropTable(name: "points_giveaways");

            migrationBuilder.DropTable(name: "whisper_quota_buckets");

            migrationBuilder.DropTable(name: "custom_announcement_delivery_policies");

            migrationBuilder.DropTable(name: "custom_message_library_entries");

            migrationBuilder.DropTable(name: "guess_round_profiles");

            migrationBuilder.DropTable(name: "hosts");
        }
    }
}
