using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v020 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TwitchMessageId",
                table: "public_chat_send_receipts",
                type: "TEXT",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "StartupMessageEnabled",
                table: "hosts",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "StartupMessageText",
                table: "hosts",
                type: "TEXT",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "InvocationLimit",
                table: "custom_commands",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unlimited"
            );

            migrationBuilder.CreateTable(
                name: "active_public_chat_pins",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TwitchMessageId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PinnerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReplyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OwnerId = table.Column<long>(type: "INTEGER", nullable: false),
                    UnpinOnOwnerCompletion = table.Column<bool>(type: "INTEGER", nullable: false),
                    PinnedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "custom_command_invocation_claims",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomCommandId = table.Column<int>(type: "INTEGER", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    TwitchStreamId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_invocation_claims", x => x.Id);
                    table.CheckConstraint(
                        "CK_custom_command_invocation_claims_Scope",
                        "(TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL) OR (TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL) OR (TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_invocation_claims_custom_commands_HostId_CustomCommandId",
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomCommandId = table.Column<int>(type: "INTEGER", nullable: true),
                    CommandName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    TargetLogin = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AffectedClaimCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ResetAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_invocation_reset_audits", x => x.Id);
                    table.CheckConstraint(
                        "CK_custom_command_invocation_reset_audits_Scope",
                        "Scope IN ('AllViewers', 'OneViewer')"
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_invocation_reset_audits_custom_commands_CustomCommandId",
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
                name: "public_chat_pin_operations",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OutboxMessageId = table.Column<long>(type: "INTEGER", nullable: true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReplyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OwnerId = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    UnpinOnOwnerCompletion = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwitchMessageId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PinnerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AttemptStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_chat_pin_operations", x => x.Id);
                    table.CheckConstraint(
                        "CK_public_chat_pin_operations_DurationSeconds",
                        "DurationSeconds IS NULL OR DurationSeconds BETWEEN 30 AND 1800"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_pin_operations_Kind",
                        "Kind IN ('Pin', 'Unpin')"
                    );
                    table.CheckConstraint(
                        "CK_public_chat_pin_operations_Status",
                        "Status IN ('Attempting', 'AwaitingDelivery', 'NoOp', 'Ready', 'Succeeded', 'Terminal')"
                    );
                    table.ForeignKey(
                        name: "FK_public_chat_pin_operations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_public_chat_pin_operations_public_chat_outbox_OutboxMessageId",
                        column: x => x.OutboxMessageId,
                        principalTable: "public_chat_outbox",
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReplyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    UnpinOnOwnerCompletion = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reply_pin_policies", x => x.Id);
                    table.CheckConstraint(
                        "CK_reply_pin_policies_DurationSeconds",
                        "DurationSeconds IS NULL OR DurationSeconds BETWEEN 30 AND 1800"
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_commands_InvocationLimit",
                table: "custom_commands",
                sql: "InvocationLimit IN ('OncePerStream', 'OncePerStreamPerUser', 'OncePerUser', 'Unlimited')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_announcements_AnnouncementColor",
                table: "custom_announcements",
                sql: "AnnouncementColor IN ('Blue', 'Green', 'Orange', 'Primary', 'Purple')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_announcements_DeliveryType",
                table: "custom_announcements",
                sql: "DeliveryType IN ('ChatMessage', 'TwitchAnnouncement')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_custom_announcements_LatestDeliveryResult",
                table: "custom_announcements",
                sql: "LatestDeliveryResult IN ('Ambiguous', 'Invalid', 'None', 'Permission', 'RateLimitRetry', 'Success', 'Unexpected')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_active_public_chat_pins_HostId_Channel",
                table: "active_public_chat_pins",
                columns: new[] { "HostId", "Channel" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_invocation_claims_HostId_CustomCommandId_TwitchStreamId",
                table: "custom_command_invocation_claims",
                columns: new[] { "HostId", "CustomCommandId", "TwitchStreamId" },
                unique: true,
                filter: "TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_invocation_claims_HostId_CustomCommandId_TwitchUserId",
                table: "custom_command_invocation_claims",
                columns: new[] { "HostId", "CustomCommandId", "TwitchUserId" },
                unique: true,
                filter: "TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_invocation_claims_HostId_CustomCommandId_TwitchUserId_TwitchStreamId",
                table: "custom_command_invocation_claims",
                columns: new[] { "HostId", "CustomCommandId", "TwitchUserId", "TwitchStreamId" },
                unique: true,
                filter: "TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL"
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
                name: "IX_reply_pin_policies_HostId_Feature_ReplyKey",
                table: "reply_pin_policies",
                columns: new[] { "HostId", "Feature", "ReplyKey" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "active_public_chat_pins");

            migrationBuilder.DropTable(name: "custom_command_invocation_claims");

            migrationBuilder.DropTable(name: "custom_command_invocation_reset_audits");

            migrationBuilder.DropTable(name: "public_chat_pin_operations");

            migrationBuilder.DropTable(name: "reply_pin_policies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_commands_InvocationLimit",
                table: "custom_commands"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_announcements_AnnouncementColor",
                table: "custom_announcements"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_announcements_DeliveryType",
                table: "custom_announcements"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_custom_announcements_LatestDeliveryResult",
                table: "custom_announcements"
            );

            migrationBuilder.DropColumn(
                name: "TwitchMessageId",
                table: "public_chat_send_receipts"
            );

            migrationBuilder.DropColumn(name: "StartupMessageEnabled", table: "hosts");

            migrationBuilder.DropColumn(name: "StartupMessageText", table: "hosts");

            migrationBuilder.DropColumn(name: "InvocationLimit", table: "custom_commands");
        }
    }
}
