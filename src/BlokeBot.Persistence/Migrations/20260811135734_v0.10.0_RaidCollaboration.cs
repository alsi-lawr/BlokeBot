using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_RaidCollaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RaidCollaborationAcceptEventsAfterUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "RaidCollaborationPausedAtUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "approved_raid_channels",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
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
                    ApprovedClipId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "raid_collaboration_history",
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
                    Direction = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OtherTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    OtherLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    OtherDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProviderStreamId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WelcomeOutcome = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    ShoutoutOutcome = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raid_collaboration_history", x => x.Id);
                    table.CheckConstraint(
                        "CK_raid_collaboration_history_Direction",
                        "Direction IN ('Incoming', 'Outgoing')"
                    );
                    table.CheckConstraint(
                        "CK_raid_collaboration_history_ShoutoutOutcome",
                        "ShoutoutOutcome IN ('Cooldown', 'Deduplicated', 'NotConfigured', 'Rejected', 'Sent', 'Suppressed', 'TargetOffline')"
                    );
                    table.CheckConstraint(
                        "CK_raid_collaboration_history_WelcomeOutcome",
                        "WelcomeOutcome IN ('Deduplicated', 'Delivered', 'NotConfigured', 'Rejected', 'Suppressed')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    WelcomeEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    WelcomeMessage = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false,
                        defaultValue: "Welcome {display_name} and community!"
                    ),
                    NativeShoutoutEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    DeduplicationWindowMinutes = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 60
                    ),
                    Language = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false,
                        defaultValue: "en"
                    ),
                    EligibleCategories = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    RelationshipCooldownHours = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 336
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_approved_raid_channels_HostId_Login",
                table: "approved_raid_channels",
                columns: new[] { "HostId", "Login" },
                unique: true
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "approved_raid_channels");

            migrationBuilder.DropTable(name: "raid_collaboration_history");

            migrationBuilder.DropTable(name: "raid_collaboration_settings");

            migrationBuilder.DropColumn(
                name: "RaidCollaborationAcceptEventsAfterUtc",
                table: "hosts"
            );

            migrationBuilder.DropColumn(name: "RaidCollaborationPausedAtUtc", table: "hosts");
        }
    }
}
