using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v040_Moments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "point_ledger_entries",
                type: "TEXT",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "moment_candidates",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    StreamIdentity = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TwitchClipId = table.Column<int>(type: "INTEGER", nullable: true),
                    TwitchStreamMarkerId = table.Column<int>(type: "INTEGER", nullable: true),
                    PublicTitle = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    PublicCategory = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    ProviderFailureReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    PrivateRejectionReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    MergedIntoCandidateId = table.Column<long>(type: "INTEGER", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_candidates", x => x.Id);
                    table.CheckConstraint(
                        "CK_moment_candidates_State",
                        "State IN ('Approved', 'ClipReady', 'Failed', 'MarkerReady', 'Merged', 'ProviderPending', 'Rejected')"
                    );
                    table.ForeignKey(
                        name: "FK_moment_candidates_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_candidates_moment_candidates_MergedIntoCandidateId",
                        column: x => x.MergedIntoCandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_moment_candidates_twitch_clips_TwitchClipId",
                        column: x => x.TwitchClipId,
                        principalTable: "twitch_clips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_moment_candidates_twitch_stream_markers_TwitchStreamMarkerId",
                        column: x => x.TwitchStreamMarkerId,
                        principalTable: "twitch_stream_markers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_hub_settings",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    MergeWindowSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MarkerFallbackEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RewardPolicy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    RewardAmount = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_hub_settings", x => x.Id);
                    table.CheckConstraint(
                        "CK_moment_hub_settings_MergeWindowSeconds",
                        "MergeWindowSeconds BETWEEN 15 AND 300"
                    );
                    table.CheckConstraint(
                        "CK_moment_hub_settings_RewardPolicy",
                        "RewardPolicy IN ('AllContributors', 'FirstRequester', 'None')"
                    );
                    table.ForeignKey(
                        name: "FK_moment_hub_settings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_capture_requests",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_capture_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_capture_requests_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_contributors",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    NormalizedLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    CaptureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstCapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_contributors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_contributors_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StreamIdentity = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PublicPayload = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1024,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_moment_events_Kind",
                        "Kind IN ('Approved', 'Captured', 'Winner')"
                    );
                    table.ForeignKey(
                        name: "FK_moment_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_events_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_merges",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceCandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetCandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateText = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    MergedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_merges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_merges_moment_candidates_SourceCandidateId",
                        column: x => x.SourceCandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_moment_merges_moment_candidates_TargetCandidateId",
                        column: x => x.TargetCandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_moderation_audit",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PrivateText = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_moderation_audit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_moderation_audit_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_suggestions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    SuggestedTitle = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    SuggestedCategory = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_suggestions_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_votes",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    NormalizedLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_votes_moment_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "moment_weekly_finalizations",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    WeekStartsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WinningCandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_weekly_finalizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moment_weekly_finalizations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_weekly_finalizations_moment_candidates_WinningCandidateId",
                        column: x => x.WinningCandidateId,
                        principalTable: "moment_candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_HostId_OperationKey",
                table: "point_ledger_entries",
                columns: new[] { "HostId", "OperationKey" },
                unique: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund', 'MomentReward')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_HostId_StreamIdentity_LastCapturedAtUtc",
                table: "moment_candidates",
                columns: new[] { "HostId", "StreamIdentity", "LastCapturedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_MergedIntoCandidateId",
                table: "moment_candidates",
                column: "MergedIntoCandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_PublicId",
                table: "moment_candidates",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_TwitchClipId",
                table: "moment_candidates",
                column: "TwitchClipId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_candidates_TwitchStreamMarkerId",
                table: "moment_candidates",
                column: "TwitchStreamMarkerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_capture_requests_CandidateId_CapturedAtUtc_Id",
                table: "moment_capture_requests",
                columns: new[] { "CandidateId", "CapturedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_contributors_CandidateId_FirstCapturedAtUtc_Id",
                table: "moment_contributors",
                columns: new[] { "CandidateId", "FirstCapturedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_contributors_CandidateId_IdentityKey",
                table: "moment_contributors",
                columns: new[] { "CandidateId", "IdentityKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_events_CandidateId",
                table: "moment_events",
                column: "CandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_events_HostId_Id",
                table: "moment_events",
                columns: new[] { "HostId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_hub_settings_HostId",
                table: "moment_hub_settings",
                column: "HostId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_merges_HostId_TargetCandidateId_MergedAtUtc",
                table: "moment_merges",
                columns: new[] { "HostId", "TargetCandidateId", "MergedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_merges_SourceCandidateId",
                table: "moment_merges",
                column: "SourceCandidateId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_merges_TargetCandidateId",
                table: "moment_merges",
                column: "TargetCandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_moderation_audit_CandidateId",
                table: "moment_moderation_audit",
                column: "CandidateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_moderation_audit_HostId_CandidateId_Id",
                table: "moment_moderation_audit",
                columns: new[] { "HostId", "CandidateId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_suggestions_CandidateId_CreatedAtUtc_Id",
                table: "moment_suggestions",
                columns: new[] { "CandidateId", "CreatedAtUtc", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_votes_CandidateId_IdentityKey",
                table: "moment_votes",
                columns: new[] { "CandidateId", "IdentityKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_weekly_finalizations_HostId_WeekStartsAtUtc",
                table: "moment_weekly_finalizations",
                columns: new[] { "HostId", "WeekStartsAtUtc" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_weekly_finalizations_WinningCandidateId",
                table: "moment_weekly_finalizations",
                column: "WinningCandidateId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "moment_capture_requests");

            migrationBuilder.DropTable(name: "moment_contributors");

            migrationBuilder.DropTable(name: "moment_events");

            migrationBuilder.DropTable(name: "moment_hub_settings");

            migrationBuilder.DropTable(name: "moment_merges");

            migrationBuilder.DropTable(name: "moment_moderation_audit");

            migrationBuilder.DropTable(name: "moment_suggestions");

            migrationBuilder.DropTable(name: "moment_votes");

            migrationBuilder.DropTable(name: "moment_weekly_finalizations");

            migrationBuilder.DropTable(name: "moment_candidates");

            migrationBuilder.DropIndex(
                name: "IX_point_ledger_entries_HostId_OperationKey",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropColumn(name: "OperationKey", table: "point_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund')"
            );
        }
    }
}
