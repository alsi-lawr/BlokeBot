using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_Competitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CompetitionsAcceptWorkAfterUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CompetitionsPausedAtUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "competitions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: false
                    ),
                    Format = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntryKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Seeding = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Tiebreak = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamSize = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumPoints = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    WinPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    DrawPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    LossPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    Seed = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AlgorithmVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    ReminderHoursBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    ReminderMessage = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    WinnerPoints = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    RunnerUpPoints = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    WinnerAchievementKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    RunnerUpAchievementKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    PrivateLobbyInformation = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RegistrationOpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competitions", x => x.Id);
                    table.UniqueConstraint(
                        "AK_competitions_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint("CK_competitions_Capacity", "Capacity BETWEEN 2 AND 128");
                    table.CheckConstraint("CK_competitions_DrawPoints", "DrawPoints >= 0");
                    table.CheckConstraint("CK_competitions_LossPoints", "LossPoints >= 0");
                    table.CheckConstraint("CK_competitions_Revision", "Revision > 0");
                    table.CheckConstraint("CK_competitions_TeamSize", "TeamSize BETWEEN 1 AND 32");
                    table.CheckConstraint("CK_competitions_WinPoints", "WinPoints >= 0");
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
                name: "competition_audits",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompetitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
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
                    PrivateReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    PreviousScoreA = table.Column<int>(type: "INTEGER", nullable: true),
                    PreviousScoreB = table.Column<int>(type: "INTEGER", nullable: true),
                    PreviousWinnerEntrantId = table.Column<long>(type: "INTEGER", nullable: true),
                    NewScoreA = table.Column<int>(type: "INTEGER", nullable: true),
                    NewScoreB = table.Column<int>(type: "INTEGER", nullable: true),
                    NewWinnerEntrantId = table.Column<long>(type: "INTEGER", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompetitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    RegistrationOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SeedRank = table.Column<int>(type: "INTEGER", nullable: true),
                    RegisteredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompetitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    CompetitionPublicId = table.Column<string>(type: "TEXT", nullable: false),
                    OperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PublicPayload = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "competition_reward_receipts",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompetitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    EntrantId = table.Column<long>(type: "INTEGER", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Placement = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsGranted = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    AchievementKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    GrantedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AchievementGrantedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_reward_receipts", x => x.Id);
                    table.CheckConstraint(
                        "CK_competition_reward_receipts_Placement",
                        "Placement > 0"
                    );
                    table.ForeignKey(
                        name: "FK_competition_reward_receipts_competitions_HostId_CompetitionId",
                        columns: x => new { x.HostId, x.CompetitionId },
                        principalTable: "competitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "competition_entrant_members",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompetitionEntrantId = table.Column<long>(type: "INTEGER", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateContact = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_entrant_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_entrant_members_competition_entrants_HostId_CompetitionEntrantId",
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompetitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    EntrantAId = table.Column<long>(type: "INTEGER", nullable: true),
                    EntrantBId = table.Column<long>(type: "INTEGER", nullable: true),
                    ScoreA = table.Column<int>(type: "INTEGER", nullable: true),
                    ScoreB = table.Column<int>(type: "INTEGER", nullable: true),
                    WinnerEntrantId = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReminderDueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReminderDeliveredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReminderSuppressedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_matches", x => x.Id);
                    table.UniqueConstraint(
                        "AK_competition_matches_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.CheckConstraint("CK_competition_matches_Position", "Position >= 0");
                    table.CheckConstraint("CK_competition_matches_Round", "Round > 0");
                    table.CheckConstraint(
                        "CK_competition_matches_ScoreA",
                        "ScoreA IS NULL OR ScoreA >= 0"
                    );
                    table.CheckConstraint(
                        "CK_competition_matches_ScoreB",
                        "ScoreB IS NULL OR ScoreB >= 0"
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
                        name: "FK_competition_matches_competition_entrants_HostId_WinnerEntrantId",
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward', 'BingoReward', 'CompetitionReward')"
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
                name: "IX_competition_entrant_members_HostId_CompetitionEntrantId_Login",
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
                name: "IX_competition_entrants_HostId_CompetitionId_RegistrationOperationId",
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
                name: "IX_competition_matches_ReminderDueAtUtc_ReminderDeliveredAtUtc_ReminderSuppressedAtUtc",
                table: "competition_matches",
                columns: new[]
                {
                    "ReminderDueAtUtc",
                    "ReminderDeliveredAtUtc",
                    "ReminderSuppressedAtUtc",
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_competition_reward_receipts_HostId_CompetitionId_EntrantId_Login",
                table: "competition_reward_receipts",
                columns: new[] { "HostId", "CompetitionId", "EntrantId", "Login" },
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "competition_audits");

            migrationBuilder.DropTable(name: "competition_entrant_members");

            migrationBuilder.DropTable(name: "competition_events");

            migrationBuilder.DropTable(name: "competition_matches");

            migrationBuilder.DropTable(name: "competition_reward_receipts");

            migrationBuilder.DropTable(name: "competition_entrants");

            migrationBuilder.DropTable(name: "competitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropColumn(name: "CompetitionsAcceptWorkAfterUtc", table: "hosts");

            migrationBuilder.DropColumn(name: "CompetitionsPausedAtUtc", table: "hosts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward', 'BingoReward')"
            );
        }
    }
}
