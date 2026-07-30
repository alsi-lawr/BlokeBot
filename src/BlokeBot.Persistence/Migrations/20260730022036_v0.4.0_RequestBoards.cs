using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v040_RequestBoards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.AddColumn<long>(
                name: "RequestSubmissionId",
                table: "point_ledger_entries",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "request_boards",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    PointCost = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RefundPolicy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    SubmissionLimitPerUser = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmissionCooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    VoteLimitPerUser = table.Column<int>(type: "INTEGER", nullable: false),
                    VotingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrderingDescription = table.Column<string>(
                        type: "TEXT",
                        maxLength: 300,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_boards", x => x.Id);
                    table.CheckConstraint(
                        "CK_request_boards_RefundPolicy",
                        "RefundPolicy IN ('AnyUnfulfilledClosure', 'Never', 'RejectedOrWithdrawn')"
                    );
                    table.ForeignKey(
                        name: "FK_request_boards_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_board_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    BoardId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmissionId = table.Column<long>(type: "INTEGER", nullable: true),
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
                    table.PrimaryKey("PK_request_board_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_request_board_events_Kind",
                        "Kind IN ('BoardConfigured', 'Merged', 'PointsRefunded', 'PointsReserved', 'StatusChanged', 'Submitted', 'Voted')"
                    );
                    table.ForeignKey(
                        name: "FK_request_board_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_request_board_events_request_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "request_boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_board_fields",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BoardId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaximumLength = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumNumber = table.Column<decimal>(type: "TEXT", nullable: true),
                    MaximumNumber = table.Column<decimal>(type: "TEXT", nullable: true),
                    ChoiceOptions = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_board_fields", x => x.Id);
                    table.CheckConstraint(
                        "CK_request_board_fields_Kind",
                        "Kind IN ('Choice', 'Number', 'Text', 'TwitchClip', 'Url')"
                    );
                    table.ForeignKey(
                        name: "FK_request_board_fields_request_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "request_boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_submissions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    BoardId = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmitterLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedTitle = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    NormalizedUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    QueuePosition = table.Column<long>(type: "INTEGER", nullable: false),
                    VoteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicNote = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: false
                    ),
                    PrivateModeratorNote = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    PrivateRejectionReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: false
                    ),
                    PointReservationState = table.Column<string>(
                        type: "TEXT",
                        maxLength: 16,
                        nullable: false
                    ),
                    MergedIntoSubmissionId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_submissions", x => x.Id);
                    table.CheckConstraint(
                        "CK_request_submissions_PointReservationState",
                        "PointReservationState IN ('Consumed', 'None', 'Refunded', 'Reserved')"
                    );
                    table.CheckConstraint(
                        "CK_request_submissions_Status",
                        "Status IN ('Accepted', 'Approved', 'Completed', 'Merged', 'Pending', 'Queued', 'Rejected', 'Withdrawn')"
                    );
                    table.ForeignKey(
                        name: "FK_request_submissions_request_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "request_boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_request_submissions_request_submissions_MergedIntoSubmissionId",
                        column: x => x.MergedIntoSubmissionId,
                        principalTable: "request_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_submission_values",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubmissionId = table.Column<long>(type: "INTEGER", nullable: false),
                    FieldId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_submission_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_request_submission_values_request_board_fields_FieldId",
                        column: x => x.FieldId,
                        principalTable: "request_board_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_request_submission_values_request_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "request_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "request_submission_votes",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubmissionId = table.Column<long>(type: "INTEGER", nullable: false),
                    VoterLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_submission_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_request_submission_votes_request_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "request_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_point_ledger_entries_RequestSubmissionId",
                table: "point_ledger_entries",
                column: "RequestSubmissionId"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin', 'RequestReservation', 'RequestRefund')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_board_events_BoardId",
                table: "request_board_events",
                column: "BoardId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_board_events_HostId_Id",
                table: "request_board_events",
                columns: new[] { "HostId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_board_fields_BoardId_Key",
                table: "request_board_fields",
                columns: new[] { "BoardId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_board_fields_BoardId_Position",
                table: "request_board_fields",
                columns: new[] { "BoardId", "Position" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_boards_HostId_Slug",
                table: "request_boards",
                columns: new[] { "HostId", "Slug" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submission_values_FieldId",
                table: "request_submission_values",
                column: "FieldId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submission_values_SubmissionId_FieldId",
                table: "request_submission_values",
                columns: new[] { "SubmissionId", "FieldId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submission_votes_SubmissionId_VoterLogin",
                table: "request_submission_votes",
                columns: new[] { "SubmissionId", "VoterLogin" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_BoardId_NormalizedTitle",
                table: "request_submissions",
                columns: new[] { "BoardId", "NormalizedTitle" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_BoardId_NormalizedUrl",
                table: "request_submissions",
                columns: new[] { "BoardId", "NormalizedUrl" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_BoardId_Status_Priority_QueuePosition",
                table: "request_submissions",
                columns: new[] { "BoardId", "Status", "Priority", "QueuePosition" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_HostId_OperationId",
                table: "request_submissions",
                columns: new[] { "HostId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_MergedIntoSubmissionId",
                table: "request_submissions",
                column: "MergedIntoSubmissionId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "request_board_events");

            migrationBuilder.DropTable(name: "request_submission_values");

            migrationBuilder.DropTable(name: "request_submission_votes");

            migrationBuilder.DropTable(name: "request_board_fields");

            migrationBuilder.DropTable(name: "request_submissions");

            migrationBuilder.DropTable(name: "request_boards");

            migrationBuilder.DropIndex(
                name: "IX_point_ledger_entries_RequestSubmissionId",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries"
            );

            migrationBuilder.DropColumn(name: "RequestSubmissionId", table: "point_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_point_ledger_entries_Kind",
                table: "point_ledger_entries",
                sql: "Kind IN ('Add', 'Remove', 'DeleteBalance', 'TransferOut', 'TransferIn', 'GambleWin', 'GambleLoss', 'GiveawayWin', 'GuessWin')"
            );
        }
    }
}
