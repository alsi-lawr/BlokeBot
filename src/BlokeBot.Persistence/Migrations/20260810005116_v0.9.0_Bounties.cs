using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v090_Bounties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.AddColumn<long>(
                name: "BountyPledgeId",
                table: "point_ledger_entries",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "BountyRewardId",
                table: "point_ledger_entries",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "BountiesPausedAtUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "bounties",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreationFingerprint = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FailurePledgePolicy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    RewardDistribution = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    FundingTarget = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PledgedAmount = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ContributorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionReward = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bounties", x => x.Id);
                    table.UniqueConstraint("AK_bounties_HostId_Id", x => new { x.HostId, x.Id });
                    table.CheckConstraint("CK_bounties_ContributorCount", "ContributorCount >= 0");
                    table.CheckConstraint(
                        "CK_bounties_FailurePledgePolicy",
                        "FailurePledgePolicy IN ('Refund', 'Spend')"
                    );
                    table.CheckConstraint("CK_bounties_Revision", "Revision > 0");
                    table.CheckConstraint(
                        "CK_bounties_RewardDistribution",
                        "RewardDistribution IN ('Equal', 'Proportional')"
                    );
                    table.CheckConstraint(
                        "CK_bounties_Status",
                        "Status IN ('Accepted', 'Cancelled', 'Completed', 'Expired', 'Failed', 'Funding', 'Proposed')"
                    );
                    table.CheckConstraint(
                        "CK_bounties_Visibility",
                        "Visibility IN ('Private', 'Public')"
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
                name: "bounty_contributor_rewards",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    BountyId = table.Column<long>(type: "INTEGER", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Amount = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    BountyId = table.Column<long>(type: "INTEGER", nullable: false),
                    BountyPublicId = table.Column<string>(type: "TEXT", nullable: false),
                    OperationKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: true
                    ),
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
                    table.PrimaryKey("PK_bounty_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_bounty_events_Kind",
                        "Kind IN ('Accepted', 'Cancelled', 'Completed', 'Created', 'Expired', 'Extended', 'Failed', 'FundingOpened', 'FundingTargetReached', 'Pledged', 'PledgesConsumed', 'PledgesRefunded', 'RewardsDistributed')"
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    BountyId = table.Column<long>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandFingerprint = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
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
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    BountyRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bounty_moderation_audit", x => x.Id);
                    table.CheckConstraint(
                        "CK_bounty_moderation_audit_Action",
                        "Action IN ('Accepted', 'Cancelled', 'Completed', 'Created', 'Expired', 'Extended', 'Failed', 'FundingOpened', 'Rejected')"
                    );
                    table.CheckConstraint(
                        "CK_bounty_moderation_audit_FromStatus",
                        "FromStatus IN ('Accepted', 'Cancelled', 'Completed', 'Expired', 'Failed', 'Funding', 'Proposed')"
                    );
                    table.CheckConstraint(
                        "CK_bounty_moderation_audit_ToStatus",
                        "ToStatus IN ('Accepted', 'Cancelled', 'Completed', 'Expired', 'Failed', 'Funding', 'Proposed')"
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
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    BountyId = table.Column<long>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandFingerprint = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    ContributorTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ContributorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Amount = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                        "State IN ('Consumed', 'Refunded', 'Reserved')"
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

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_HostId_SubmitterLogin_PointReservationState",
                table: "request_submissions",
                columns: new[] { "HostId", "SubmitterLogin", "PointReservationState" }
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward')"
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

            migrationBuilder.AddForeignKey(
                name: "FK_point_ledger_entries_bounty_contributor_rewards_HostId_BountyRewardId",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "BountyRewardId" },
                principalTable: "bounty_contributor_rewards",
                principalColumns: new[] { "HostId", "Id" },
                onDelete: ReferentialAction.Restrict
            );

            migrationBuilder.AddForeignKey(
                name: "FK_point_ledger_entries_bounty_pledges_HostId_BountyPledgeId",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "BountyPledgeId" },
                principalTable: "bounty_pledges",
                principalColumns: new[] { "HostId", "Id" },
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_point_ledger_entries_bounty_contributor_rewards_HostId_BountyRewardId",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_point_ledger_entries_bounty_pledges_HostId_BountyPledgeId",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropTable(name: "bounty_contributor_rewards");

            migrationBuilder.DropTable(name: "bounty_events");

            migrationBuilder.DropTable(name: "bounty_moderation_audit");

            migrationBuilder.DropTable(name: "bounty_pledges");

            migrationBuilder.DropTable(name: "bounties");

            migrationBuilder.DropIndex(
                name: "IX_request_submissions_HostId_SubmitterLogin_PointReservationState",
                table: "request_submissions"
            );

            migrationBuilder.DropIndex(
                name: "IX_point_ledger_entries_HostId_BountyPledgeId",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropIndex(
                name: "IX_point_ledger_entries_HostId_BountyRewardId",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropColumn(name: "BountyPledgeId", table: "point_ledger_entries");

            migrationBuilder.DropColumn(name: "BountyRewardId", table: "point_ledger_entries");

            migrationBuilder.DropColumn(name: "BountiesPausedAtUtc", table: "hosts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward')"
            );
        }
    }
}
