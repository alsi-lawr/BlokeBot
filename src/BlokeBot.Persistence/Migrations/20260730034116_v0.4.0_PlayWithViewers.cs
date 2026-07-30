using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v040_PlayWithViewers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "play_queues",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActivityName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    SelectionMode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    ShowParticipantNames = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReadinessTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    HistoryRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    SkipExclusionMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentPartyNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queues", x => x.Id);
                    table.CheckConstraint(
                        "CK_play_queues_SelectionMode",
                        "SelectionMode IN ('JoinOrder', 'LeastRecentParticipation')"
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
                name: "play_queue_entries",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueId = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    NormalizedLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadyExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PartyNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    PrivateModeratorNote = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_entries", x => x.Id);
                    table.CheckConstraint(
                        "CK_play_queue_entries_Status",
                        "Status IN ('AwaitingReady', 'Left', 'NoShow', 'Ready', 'Selected', 'Skipped', 'Waiting')"
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueId = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PublicPayload = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1024,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_play_queue_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_play_queue_events_Kind",
                        "Kind IN ('Joined', 'Left', 'NoShow', 'PartySelected', 'QueueClosed', 'QueueConfigured', 'Ready', 'ReadyCheckStarted', 'Skipped')"
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueId = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrivateReason = table.Column<string>(
                        type: "TEXT",
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QueueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    Choices = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueId = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    ParticipatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QueueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MinimumCount = table.Column<int>(type: "INTEGER", nullable: false),
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
                name: "play_queue_entry_values",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntryId = table.Column<long>(type: "INTEGER", nullable: false),
                    FieldId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
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
                name: "IX_play_queue_participation_QueueId_IdentityKey_ParticipatedAtUtc",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "play_queue_entry_values");

            migrationBuilder.DropTable(name: "play_queue_events");

            migrationBuilder.DropTable(name: "play_queue_exclusions");

            migrationBuilder.DropTable(name: "play_queue_participation");

            migrationBuilder.DropTable(name: "play_queue_role_requirements");

            migrationBuilder.DropTable(name: "play_queue_entries");

            migrationBuilder.DropTable(name: "play_queue_fields");

            migrationBuilder.DropTable(name: "play_queues");
        }
    }
}
