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
                name: "host_broadcaster_authorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProtectedTokenPayload = table.Column<byte[]>(type: "BLOB", nullable: true),
                    TwitchUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AuthorizedScopes = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_broadcaster_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_host_broadcaster_authorizations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "twitch_poll_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelPointsVotingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChannelPointsPerVote = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_poll_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_poll_templates_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "twitch_polls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderPollId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ChoicesJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsExternallyStarted = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_polls", x => x.Id);
                    table.CheckConstraint("CK_twitch_polls_Status", "Status IN ('Active', 'Archived', 'Completed', 'Terminated')");
                    table.ForeignKey(
                        name: "FK_twitch_polls_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "twitch_poll_template_choices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TwitchPollTemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 25, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_poll_template_choices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_poll_template_choices_twitch_poll_templates_TwitchPollTemplateId",
                        column: x => x.TwitchPollTemplateId,
                        principalTable: "twitch_poll_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_host_broadcaster_authorizations_HostId",
                table: "host_broadcaster_authorizations",
                column: "HostId",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_twitch_poll_template_choices_TwitchPollTemplateId_Position",
                table: "twitch_poll_template_choices",
                columns: new[] { "TwitchPollTemplateId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_twitch_poll_templates_HostId",
                table: "twitch_poll_templates",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_twitch_polls_HostId",
                table: "twitch_polls",
                column: "HostId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_twitch_polls_HostId_EndedAtUtc",
                table: "twitch_polls",
                columns: new[] { "HostId", "EndedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_twitch_polls_HostId_ProviderPollId",
                table: "twitch_polls",
                columns: new[] { "HostId", "ProviderPollId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_broadcaster_authorizations");

            migrationBuilder.DropTable(
                name: "shoutout_cooldowns");

            migrationBuilder.DropTable(
                name: "shoutout_history");

            migrationBuilder.DropTable(
                name: "twitch_poll_template_choices");

            migrationBuilder.DropTable(
                name: "twitch_polls");

            migrationBuilder.DropTable(
                name: "twitch_poll_templates");
        }
    }
}
