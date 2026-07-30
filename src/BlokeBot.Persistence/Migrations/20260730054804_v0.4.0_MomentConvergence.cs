using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v040_MomentConvergence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "moment_events",
                type: "TEXT",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE moment_contributors AS retained
                SET CaptureCount = (
                        SELECT SUM(candidate_rows.CaptureCount)
                        FROM moment_contributors AS candidate_rows
                        WHERE candidate_rows.CandidateId = retained.CandidateId
                          AND candidate_rows.NormalizedLogin = retained.NormalizedLogin
                    ),
                    FirstCapturedAtUtc = (
                        SELECT MIN(candidate_rows.FirstCapturedAtUtc)
                        FROM moment_contributors AS candidate_rows
                        WHERE candidate_rows.CandidateId = retained.CandidateId
                          AND candidate_rows.NormalizedLogin = retained.NormalizedLogin
                    ),
                    LastCapturedAtUtc = (
                        SELECT MAX(candidate_rows.LastCapturedAtUtc)
                        FROM moment_contributors AS candidate_rows
                        WHERE candidate_rows.CandidateId = retained.CandidateId
                          AND candidate_rows.NormalizedLogin = retained.NormalizedLogin
                    )
                WHERE retained.Id = (
                    SELECT preferred.Id
                    FROM moment_contributors AS preferred
                    WHERE preferred.CandidateId = retained.CandidateId
                      AND preferred.NormalizedLogin = retained.NormalizedLogin
                    ORDER BY
                        CASE
                            WHEN preferred.TwitchUserId IS NULL OR preferred.TwitchUserId = ''
                                THEN 1
                            ELSE 0
                        END,
                        preferred.Id
                    LIMIT 1
                );

                DELETE FROM moment_contributors
                WHERE Id IN (
                    SELECT Id
                    FROM (
                        SELECT
                            Id,
                            ROW_NUMBER() OVER (
                                PARTITION BY CandidateId, NormalizedLogin
                                ORDER BY
                                    CASE
                                        WHEN TwitchUserId IS NULL OR TwitchUserId = '' THEN 1
                                        ELSE 0
                                    END,
                                    Id
                            ) AS duplicate_rank
                        FROM moment_contributors
                    )
                    WHERE duplicate_rank > 1
                );

                UPDATE moment_votes AS retained
                SET CreatedAtUtc = (
                    SELECT MIN(candidate_rows.CreatedAtUtc)
                    FROM moment_votes AS candidate_rows
                    WHERE candidate_rows.CandidateId = retained.CandidateId
                      AND candidate_rows.NormalizedLogin = retained.NormalizedLogin
                )
                WHERE retained.Id = (
                    SELECT preferred.Id
                    FROM moment_votes AS preferred
                    WHERE preferred.CandidateId = retained.CandidateId
                      AND preferred.NormalizedLogin = retained.NormalizedLogin
                    ORDER BY
                        CASE
                            WHEN preferred.TwitchUserId IS NULL OR preferred.TwitchUserId = ''
                                THEN 1
                            ELSE 0
                        END,
                        preferred.Id
                    LIMIT 1
                );

                DELETE FROM moment_votes
                WHERE Id IN (
                    SELECT Id
                    FROM (
                        SELECT
                            Id,
                            ROW_NUMBER() OVER (
                                PARTITION BY CandidateId, NormalizedLogin
                                ORDER BY
                                    CASE
                                        WHEN TwitchUserId IS NULL OR TwitchUserId = '' THEN 1
                                        ELSE 0
                                    END,
                                    Id
                            ) AS duplicate_rank
                        FROM moment_votes
                    )
                    WHERE duplicate_rank > 1
                );
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_votes_CandidateId_NormalizedLogin",
                table: "moment_votes",
                columns: new[] { "CandidateId", "NormalizedLogin" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_events_HostId_OperationKey",
                table: "moment_events",
                columns: new[] { "HostId", "OperationKey" },
                unique: true,
                filter: "\"OperationKey\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_contributors_CandidateId_NormalizedLogin",
                table: "moment_contributors",
                columns: new[] { "CandidateId", "NormalizedLogin" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_moment_votes_CandidateId_NormalizedLogin",
                table: "moment_votes"
            );

            migrationBuilder.DropIndex(
                name: "IX_moment_events_HostId_OperationKey",
                table: "moment_events"
            );

            migrationBuilder.DropIndex(
                name: "IX_moment_contributors_CandidateId_NormalizedLogin",
                table: "moment_contributors"
            );

            migrationBuilder.DropColumn(name: "OperationKey", table: "moment_events");
        }
    }
}
