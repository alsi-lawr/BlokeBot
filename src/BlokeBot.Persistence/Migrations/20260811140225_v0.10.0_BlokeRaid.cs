using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_BlokeRaid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "BlokeRaidAcceptWorkAfterUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "BlokeRaidPausedAtUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "bloke_raid_campaigns",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    StartOperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BossName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    MaximumHealth = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentHealth = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumWard = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentWard = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentPhase = table.Column<int>(type: "INTEGER", nullable: false),
                    VictoryPointReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ResetPolicy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VictoryRewardedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
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
                        "ResetPolicy IN ('Manual', 'Weekly')"
                    );
                    table.CheckConstraint(
                        "CK_bloke_raid_campaigns_Status",
                        "Status IN ('Active', 'Ended', 'Expired', 'Victory')"
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
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    BossName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    MaximumHealth = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumWard = table.Column<int>(type: "INTEGER", nullable: false),
                    CampaignDurationHours = table.Column<int>(type: "INTEGER", nullable: false),
                    AttackMinimum = table.Column<int>(type: "INTEGER", nullable: false),
                    AttackMaximum = table.Column<int>(type: "INTEGER", nullable: false),
                    AttackCooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    AttackPerStreamLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    MendMinimum = table.Column<int>(type: "INTEGER", nullable: false),
                    MendMaximum = table.Column<int>(type: "INTEGER", nullable: false),
                    MendCooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MendPerStreamLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecialMinimum = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecialMaximum = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecialCooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecialPerStreamLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecialPointCost = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    CorrectGuessDamage = table.Column<int>(type: "INTEGER", nullable: false),
                    VictoryPointReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PhaseTwoHealthPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    PhaseThreeHealthPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    PhaseOneResponse = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    PhaseTwoResponse = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    PhaseThreeResponse = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    VictoryResponse = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    ExpiryResponse = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    ResetPolicy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    WeeklyResetDay = table.Column<int>(type: "INTEGER", nullable: false),
                    WeeklyResetHourUtc = table.Column<int>(type: "INTEGER", nullable: false),
                    NextWeeklyResetAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_configurations", x => x.Id);
                    table.CheckConstraint(
                        "CK_bloke_raid_configurations_ResetPolicy",
                        "ResetPolicy IN ('Manual', 'Weekly')"
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
                name: "bloke_raid_actions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: false),
                    OperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
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
                        maxLength: 128,
                        nullable: true
                    ),
                    StreamKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    PointCost = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BossHealthBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    BossHealthAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    WardBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    WardAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    PhaseAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    GuessRoundId = table.Column<int>(type: "INTEGER", nullable: true),
                    Response = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_actions", x => x.Id);
                    table.CheckConstraint(
                        "CK_bloke_raid_actions_Kind",
                        "Kind IN ('Attack', 'CorrectGuess', 'Mend', 'Special')"
                    );
                    table.CheckConstraint(
                        "CK_bloke_raid_actions_Source",
                        "Source IN ('Chat', 'Guessing')"
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: false),
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
                        maxLength: 128,
                        nullable: false
                    ),
                    Damage = table.Column<int>(type: "INTEGER", nullable: false),
                    WardRestored = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecialCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectGuessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastContributedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_contributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bloke_raid_contributions_bloke_raid_campaigns_HostId_CampaignId",
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "TEXT",
                        maxLength: 4096,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloke_raid_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_bloke_raid_events_Kind",
                        "Kind IN ('ActionResolved', 'CampaignEnded', 'CampaignExpired', 'CampaignReset', 'CampaignStarted', 'CampaignVictorious', 'PhaseChanged')"
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward', 'BingoReward', 'CompetitionReward', 'BlokeRaidSpecialSpend', 'BlokeRaidVictoryReward')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_bloke_raid_actions_HostId_CampaignId_ViewerTwitchUserId_Kind_OccurredAtUtc",
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
                name: "IX_bloke_raid_contributions_HostId_CampaignId_ViewerTwitchUserId",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "bloke_raid_actions");

            migrationBuilder.DropTable(name: "bloke_raid_configurations");

            migrationBuilder.DropTable(name: "bloke_raid_contributions");

            migrationBuilder.DropTable(name: "bloke_raid_events");

            migrationBuilder.DropTable(name: "bloke_raid_campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropColumn(name: "BlokeRaidAcceptWorkAfterUtc", table: "hosts");

            migrationBuilder.DropColumn(name: "BlokeRaidPausedAtUtc", table: "hosts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward', 'BingoReward', 'CompetitionReward')"
            );
        }
    }
}
