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
                name: "twitch_clips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProviderClipId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EditUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    FinalUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    BroadcasterTwitchUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    BroadcasterLogin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatorTwitchUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatorLogin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    VideoId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_clips", x => x.Id);
                    table.CheckConstraint("CK_twitch_clips_Status", "Status IN ('Ambiguous', 'Available', 'Expired', 'Failed', 'Pending')");
                    table.ForeignKey(
                        name: "FK_twitch_clips_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "twitch_custom_rewards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderRewardId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Cost = table.Column<int>(type: "INTEGER", nullable: false),
                    IsManageable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPaused = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsUserInputRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMaxPerStreamEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxPerStream = table.Column<int>(type: "INTEGER", nullable: true),
                    IsMaxPerUserPerStreamEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxPerUserPerStream = table.Column<int>(type: "INTEGER", nullable: true),
                    IsGlobalCooldownEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GlobalCooldownSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ShouldRedemptionsSkipRequestQueue = table.Column<bool>(type: "INTEGER", nullable: false),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_custom_rewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_custom_rewards_hosts_HostId",
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
                name: "twitch_reward_redemptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderRedemptionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderRewardId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RewardTitle = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserLogin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserInput = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_reward_redemptions", x => x.Id);
                    table.CheckConstraint("CK_twitch_reward_redemptions_Status", "Status IN ('Canceled', 'Fulfilled', 'Unfulfilled')");
                    table.ForeignKey(
                        name: "FK_twitch_reward_redemptions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "twitch_stream_markers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProviderMarkerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 140, nullable: false),
                    PositionSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MarkerUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    VideoId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EnrichedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_stream_markers", x => x.Id);
                    table.CheckConstraint("CK_twitch_stream_markers_Status", "Status IN ('Ambiguous', 'Failed', 'Succeeded')");
                    table.ForeignKey(
                        name: "FK_twitch_stream_markers_hosts_HostId",
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
                name: "IX_twitch_clips_HostId_IdempotencyKey",
                table: "twitch_clips",
                columns: new[] { "HostId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_twitch_clips_HostId_Status_ResolvedAtUtc",
                table: "twitch_clips",
                columns: new[] { "HostId", "Status", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_twitch_custom_rewards_HostId_ProviderRewardId",
                table: "twitch_custom_rewards",
                columns: new[] { "HostId", "ProviderRewardId" },
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

            migrationBuilder.CreateIndex(
                name: "IX_twitch_reward_redemptions_HostId_ProviderRedemptionId",
                table: "twitch_reward_redemptions",
                columns: new[] { "HostId", "ProviderRedemptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_twitch_reward_redemptions_HostId_Status_UpdatedAtUtc",
                table: "twitch_reward_redemptions",
                columns: new[] { "HostId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_twitch_stream_markers_HostId_CreatedAtUtc",
                table: "twitch_stream_markers",
                columns: new[] { "HostId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_twitch_stream_markers_HostId_IdempotencyKey",
                table: "twitch_stream_markers",
                columns: new[] { "HostId", "IdempotencyKey" },
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
                name: "twitch_clips");

            migrationBuilder.DropTable(
                name: "twitch_custom_rewards");

            migrationBuilder.DropTable(
                name: "twitch_poll_template_choices");

            migrationBuilder.DropTable(
                name: "twitch_polls");

            migrationBuilder.DropTable(
                name: "twitch_reward_redemptions");

            migrationBuilder.DropTable(
                name: "twitch_stream_markers");

            migrationBuilder.DropTable(
                name: "twitch_poll_templates");
        }
    }
}
