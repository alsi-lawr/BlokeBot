using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v090_Bingo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_event_feed_items_Kind",
                table: "overlay_event_feed_items"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "BingoAcceptEventsAfterUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "BingoPausedAtUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "bingo_event_receipts",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceEventId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 240,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardId = table.Column<long>(type: "INTEGER", nullable: true),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardId = table.Column<long>(type: "INTEGER", nullable: true),
                    MarkId = table.Column<long>(type: "INTEGER", nullable: true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CurrentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "bingo_template_revisions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    FullCardWinEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinePointsReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    LineAchievementKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: true
                    ),
                    FullCardPointsReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    FullCardAchievementKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: true
                    ),
                    CreatedByTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedByLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_template_revisions", x => x.Id);
                    table.CheckConstraint(
                        "CK_bingo_template_revisions_Dimension",
                        "Dimension IN (3, 4, 5)"
                    );
                    table.CheckConstraint("CK_bingo_template_revisions_Revision", "Revision > 0");
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
                name: "bingo_games",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateRevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    TemplateName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    TemplateRevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    Seed = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ParticipantCap = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamCap = table.Column<int>(type: "INTEGER", nullable: true),
                    FullCardWinEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinePointsReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    LineAchievementKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: true
                    ),
                    FullCardPointsReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    FullCardAchievementKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bingo_games", x => x.Id);
                    table.CheckConstraint("CK_bingo_games_Dimension", "Dimension IN (3, 4, 5)");
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateRevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Threshold = table.Column<long>(type: "INTEGER", nullable: true),
                    FilterToken = table.Column<string>(
                        type: "TEXT",
                        maxLength: 240,
                        nullable: true
                    ),
                    PrivateModeratorNote = table.Column<string>(
                        type: "TEXT",
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
                name: "bingo_cards",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    AssignmentKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 240,
                        nullable: false
                    ),
                    AssignmentName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    IssuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
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
                name: "bingo_marks",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    SquareKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstMarkedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RuleIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PointsReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    AchievementKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: true
                    ),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RewardsCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<long>(type: "INTEGER", nullable: true),
                    CardId = table.Column<long>(type: "INTEGER", nullable: true),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    JoinedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    MarkId = table.Column<long>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EventKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ParticipantTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ParticipantLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    ParticipantDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: true
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    WinId = table.Column<long>(type: "INTEGER", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    PointsGranted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AchievementGranted = table.Column<bool>(type: "INTEGER", nullable: false),
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward', 'BingoReward')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_event_feed_items_Kind",
                table: "overlay_event_feed_items",
                sql: "Kind IN ('bingoEvent', 'giveawayWinner', 'guessingWinner', 'pointAward')"
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "bingo_event_receipts");

            migrationBuilder.DropTable(name: "bingo_events");

            migrationBuilder.DropTable(name: "bingo_evidence");

            migrationBuilder.DropTable(name: "bingo_moderation_audit");

            migrationBuilder.DropTable(name: "bingo_participants");

            migrationBuilder.DropTable(name: "bingo_squares");

            migrationBuilder.DropTable(name: "bingo_win_recipients");

            migrationBuilder.DropTable(name: "bingo_marks");

            migrationBuilder.DropTable(name: "bingo_teams");

            migrationBuilder.DropTable(name: "bingo_wins");

            migrationBuilder.DropTable(name: "bingo_cards");

            migrationBuilder.DropTable(name: "bingo_games");

            migrationBuilder.DropTable(name: "bingo_template_revisions");

            migrationBuilder.DropTable(name: "bingo_templates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_event_feed_items_Kind",
                table: "overlay_event_feed_items"
            );

            migrationBuilder.DropColumn(name: "BingoAcceptEventsAfterUtc", table: "hosts");

            migrationBuilder.DropColumn(name: "BingoPausedAtUtc", table: "hosts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_event_feed_items_Kind",
                table: "overlay_event_feed_items",
                sql: "Kind IN ('giveawayWinner', 'guessingWinner', 'pointAward')"
            );
        }
    }
}
