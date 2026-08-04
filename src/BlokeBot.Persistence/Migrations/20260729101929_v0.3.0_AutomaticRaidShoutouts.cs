#pragma warning disable

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V030AutomaticRaidShoutouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automatic_raid_processed_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderMessageId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_raid_processed_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_automatic_raid_processed_events_Expiry",
                        "ExpiresAtUtc >= ClaimedAtUtc"
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderMessageId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    SourceTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    SourceLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    SourceDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResultCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    MessageTimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_raid_shoutout_outcomes", x => x.Id);
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_ResultCode",
                        "ResultCode IS NULL OR ResultCode IN ('Ambiguous', 'AuthorityRequired', 'Cooldown', 'Delivered', 'Invalid', 'NotReady', 'PartialFailure', 'RateLimited', 'Rejected', 'RuntimeMessageTooLong', 'Unexpected')"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_State",
                        "(Status = 'Processing' AND ResultCode IS NULL AND CompletedAtUtc IS NULL) OR (Status = 'Delivered' AND ResultCode IS NOT NULL AND ResultCode = 'Delivered' AND CompletedAtUtc IS NOT NULL) OR (Status = 'NotDelivered' AND ResultCode IS NOT NULL AND ResultCode NOT IN ('Delivered', 'Ambiguous') AND CompletedAtUtc IS NOT NULL) OR (Status = 'Ambiguous' AND ResultCode IS NOT NULL AND ResultCode = 'Ambiguous' AND CompletedAtUtc IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_outcomes_Status",
                        "Status IN ('Ambiguous', 'Delivered', 'NotDelivered', 'Processing')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: false
                    ),
                    MinimumViewerCount = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 1
                    ),
                    Mechanism = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "Native"
                    ),
                    ChatPresentation = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "Regular"
                    ),
                    MessageTemplate = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1024,
                        nullable: false,
                        defaultValue: "Welcome {display_name}! Check them out at {channel_url}"
                    ),
                    PinDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    AnnouncementColor = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "Primary"
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automatic_raid_shoutout_settings", x => x.Id);
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_AnnouncementColor",
                        "AnnouncementColor IN ('Blue', 'Green', 'Orange', 'Primary', 'Purple')"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_ChatPresentation",
                        "ChatPresentation IN ('Announcement', 'Pinned', 'Regular')"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_Mechanism",
                        "Mechanism IN ('Chat', 'Native')"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_MinimumViewerCount",
                        "MinimumViewerCount >= 1"
                    );
                    table.CheckConstraint(
                        "CK_automatic_raid_shoutout_settings_PinDuration",
                        "PinDurationSeconds IS NULL OR (PinDurationSeconds >= 30 AND PinDurationSeconds <= 1800)"
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "automatic_raid_processed_events");

            migrationBuilder.DropTable(name: "automatic_raid_shoutout_outcomes");

            migrationBuilder.DropTable(name: "automatic_raid_shoutout_settings");
        }
    }
}
