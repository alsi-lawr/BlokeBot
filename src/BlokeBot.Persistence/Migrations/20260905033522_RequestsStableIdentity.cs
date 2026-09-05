using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequestsStableIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_request_submission_votes_SubmissionId_VoterLogin",
                table: "request_submission_votes"
            );

            migrationBuilder.AddColumn<string>(
                name: "SubmitterTwitchUserId",
                table: "request_submissions",
                type: "TEXT",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "VoterTwitchUserId",
                table: "request_submission_votes",
                type: "TEXT",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submissions_HostId_BoardId_SubmitterTwitchUserId",
                table: "request_submissions",
                columns: new[] { "HostId", "BoardId", "SubmitterTwitchUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submission_votes_SubmissionId_VoterTwitchUserId",
                table: "request_submission_votes",
                columns: new[] { "SubmissionId", "VoterTwitchUserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_request_submissions_HostId_BoardId_SubmitterTwitchUserId",
                table: "request_submissions"
            );

            migrationBuilder.DropIndex(
                name: "IX_request_submission_votes_SubmissionId_VoterTwitchUserId",
                table: "request_submission_votes"
            );

            migrationBuilder.DropColumn(
                name: "SubmitterTwitchUserId",
                table: "request_submissions"
            );

            migrationBuilder.DropColumn(
                name: "VoterTwitchUserId",
                table: "request_submission_votes"
            );

            migrationBuilder.CreateIndex(
                name: "IX_request_submission_votes_SubmissionId_VoterLogin",
                table: "request_submission_votes",
                columns: new[] { "SubmissionId", "VoterLogin" },
                unique: true
            );
        }
    }
}
