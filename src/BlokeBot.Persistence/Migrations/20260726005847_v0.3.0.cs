using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v030 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shoutout_cooldowns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    GlobalEligibleAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TargetTwitchUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TargetLogin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetEligibleAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoutout_cooldowns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shoutout_cooldowns_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shoutout_history",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceTwitchUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceLogin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TargetTwitchUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetLogin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ViewerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CooldownEndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TargetCooldownEndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shoutout_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shoutout_history_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shoutout_cooldowns_HostId_TargetTwitchUserId",
                table: "shoutout_cooldowns",
                columns: new[] { "HostId", "TargetTwitchUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shoutout_history_HostId_OccurredAtUtc",
                table: "shoutout_history",
                columns: new[] { "HostId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_shoutout_history_HostId_ProviderMessageId",
                table: "shoutout_history",
                columns: new[] { "HostId", "ProviderMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shoutout_cooldowns");

            migrationBuilder.DropTable(
                name: "shoutout_history");
        }
    }
}
