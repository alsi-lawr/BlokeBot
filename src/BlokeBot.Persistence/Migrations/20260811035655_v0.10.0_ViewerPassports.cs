using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_ViewerPassports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "viewer_passports",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
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
                    ProfileLine = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    HideAttendance = table.Column<bool>(type: "INTEGER", nullable: false),
                    SelectedTitleRewardDefinitionId = table.Column<long>(
                        type: "INTEGER",
                        nullable: true
                    ),
                    SelectedBadgeRewardDefinitionId = table.Column<long>(
                        type: "INTEGER",
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passports_community_reward_definitions_SelectedBadgeRewardDefinitionId",
                        column: x => x.SelectedBadgeRewardDefinitionId,
                        principalTable: "community_reward_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passports_community_reward_definitions_SelectedTitleRewardDefinitionId",
                        column: x => x.SelectedTitleRewardDefinitionId,
                        principalTable: "community_reward_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passports_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "viewer_passport_attendance_days",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PassportId = table.Column<long>(type: "INTEGER", nullable: false),
                    DateUtc = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_attendance_days", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passport_attendance_days_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passport_attendance_days_viewer_passports_PassportId",
                        column: x => x.PassportId,
                        principalTable: "viewer_passports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_attendance_days_HostId_PassportId_DateUtc",
                table: "viewer_passport_attendance_days",
                columns: new[] { "HostId", "PassportId", "DateUtc" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_attendance_days_PassportId",
                table: "viewer_passport_attendance_days",
                column: "PassportId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passports_HostId_Login",
                table: "viewer_passports",
                columns: new[] { "HostId", "Login" },
                unique: true,
                filter: "\"Login\" <> ''"
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passports_HostId_TwitchUserId",
                table: "viewer_passports",
                columns: new[] { "HostId", "TwitchUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passports_SelectedBadgeRewardDefinitionId",
                table: "viewer_passports",
                column: "SelectedBadgeRewardDefinitionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passports_SelectedTitleRewardDefinitionId",
                table: "viewer_passports",
                column: "SelectedTitleRewardDefinitionId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "viewer_passport_attendance_days");

            migrationBuilder.DropTable(name: "viewer_passports");
        }
    }
}
