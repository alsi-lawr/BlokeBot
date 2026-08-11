using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v090_CommunityProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.AddColumn<long>(
                name: "CommunityCompletionId",
                table: "point_ledger_entries",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CommunityProgressionAcceptEventsAfterUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CommunityProgressionPausedAtUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "community_seasons",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: false
                    ),
                    ModeratorNotes = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceEventId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "community_definitions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CompletionMode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    EventRule = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Increment = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FilterToken = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: true
                    ),
                    Target = table.Column<long>(type: "INTEGER", nullable: false),
                    PointsReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ResetCadence = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    ResetLocalTime = table.Column<string>(
                        type: "TEXT",
                        maxLength: 5,
                        nullable: false
                    ),
                    ResetWeekday = table.Column<int>(type: "INTEGER", nullable: true),
                    ScheduleRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_definitions", x => x.Id);
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<long>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 240,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    PresentationToken = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<long>(type: "INTEGER", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    CompletedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgressAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshottedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "community_audits",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<long>(type: "INTEGER", nullable: true),
                    DefinitionId = table.Column<long>(type: "INTEGER", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    OperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    ActorTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateNote = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<long>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    SubjectKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: true
                    ),
                    DefinitionKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    DefinitionName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    PointsGranted = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    RewardSnapshot = table.Column<string>(
                        type: "TEXT",
                        maxLength: 4000,
                        nullable: false
                    ),
                    SourceOperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 240,
                        nullable: false
                    ),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonId = table.Column<long>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    SubjectKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: true
                    ),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    PeriodKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    RolloverKind = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    OperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                    DefinitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    RewardDefinitionId = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_community_definition_rewards",
                        x => new { x.DefinitionId, x.RewardDefinitionId }
                    );
                    table.ForeignKey(
                        name: "FK_community_definition_rewards_community_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "community_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_community_definition_rewards_community_reward_definitions_RewardDefinitionId",
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RewardDefinitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    LastOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EquippedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_equipped_rewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_equipped_rewards_community_reward_definitions_RewardDefinitionId",
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
                name: "community_external_grant_receipts",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    Fingerprint = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    CompletionId = table.Column<long>(type: "INTEGER", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_external_grant_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_external_grant_receipts_community_completions_HostId_CompletionId",
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    RewardDefinitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ViewerDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    CompletionId = table.Column<long>(type: "INTEGER", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_reward_unlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_reward_unlocks_community_completions_HostId_CompletionId",
                        columns: x => new { x.HostId, x.CompletionId },
                        principalTable: "community_completions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_community_reward_unlocks_community_reward_definitions_RewardDefinitionId",
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

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_HostId_CommunityCompletionId",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "CommunityCompletionId" }
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward')"
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
                name: "IX_community_completions_HostId_DefinitionId_SubjectKey_Sequence",
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
                name: "IX_community_external_grant_receipts_HostId_Source_IdempotencyKey",
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
                name: "IX_community_reward_unlocks_HostId_RewardDefinitionId_ViewerTwitchUserId",
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
                name: "IX_community_season_standings_HostId_SeasonId_ViewerTwitchUserId",
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
                name: "IX_community_source_event_receipts_HostId_SourceKind_SourceEventId",
                table: "community_source_event_receipts",
                columns: new[] { "HostId", "SourceKind", "SourceEventId" },
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_point_ledger_entries_community_completions_HostId_CommunityCompletionId",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "CommunityCompletionId" },
                principalTable: "community_completions",
                principalColumns: new[] { "HostId", "Id" },
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_point_ledger_entries_community_completions_HostId_CommunityCompletionId",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropTable(name: "community_audits");

            migrationBuilder.DropTable(name: "community_definition_rewards");

            migrationBuilder.DropTable(name: "community_equipped_rewards");

            migrationBuilder.DropTable(name: "community_events");

            migrationBuilder.DropTable(name: "community_external_grant_receipts");

            migrationBuilder.DropTable(name: "community_progress");

            migrationBuilder.DropTable(name: "community_reset_periods");

            migrationBuilder.DropTable(name: "community_reward_unlocks");

            migrationBuilder.DropTable(name: "community_season_standings");

            migrationBuilder.DropTable(name: "community_source_event_receipts");

            migrationBuilder.DropTable(name: "community_completions");

            migrationBuilder.DropTable(name: "community_reward_definitions");

            migrationBuilder.DropTable(name: "community_definitions");

            migrationBuilder.DropTable(name: "community_seasons");

            migrationBuilder.DropIndex(
                name: "IX_point_ledger_entries_HostId_CommunityCompletionId",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropColumn(
                name: "CommunityCompletionId",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropColumn(
                name: "CommunityProgressionAcceptEventsAfterUtc",
                table: "hosts"
            );

            migrationBuilder.DropColumn(name: "CommunityProgressionPausedAtUtc", table: "hosts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward')"
            );
        }
    }
}
