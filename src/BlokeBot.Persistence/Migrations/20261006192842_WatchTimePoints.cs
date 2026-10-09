using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WatchTimePoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "WatchTimeConfigurationRevision",
                table: "points_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.AddColumn<Guid>(
                name: "WatchTimeEnableGeneration",
                table: "points_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.AddColumn<string>(
                name: "WatchTimePointAmount",
                table: "points_settings",
                type: "TEXT",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "WatchTimePointsEnabled",
                table: "points_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_points_settings_WatchTimeEnabled",
                table: "points_settings",
                sql: "NOT \"WatchTimePointsEnabled\" OR (\"WatchTimePointAmount\" IS NOT NULL AND \"WatchTimeEnableGeneration\" <> '00000000-0000-0000-0000-000000000000')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('WatchTimeReward', 'Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward', 'BingoReward', 'CompetitionReward', 'BlokeRaidSpecialSpend', 'BlokeRaidVictoryReward')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_points_settings_WatchTimeEnabled",
                table: "points_settings"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropColumn(
                name: "WatchTimeConfigurationRevision",
                table: "points_settings"
            );

            migrationBuilder.DropColumn(
                name: "WatchTimeEnableGeneration",
                table: "points_settings"
            );

            migrationBuilder.DropColumn(name: "WatchTimePointAmount", table: "points_settings");

            migrationBuilder.DropColumn(name: "WatchTimePointsEnabled", table: "points_settings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward', 'BountyPledgeReservation', 'BountyPledgeRefund', 'BountyPledgeConsumption', 'BountyCompletionReward', 'CommunityProgressionReward', 'BingoReward', 'CompetitionReward', 'BlokeRaidSpecialSpend', 'BlokeRaidVictoryReward')"
            );
        }
    }
}
