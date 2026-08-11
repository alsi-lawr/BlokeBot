using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_MomentAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_moment_candidates_HostId_Id",
                table: "moment_candidates",
                columns: new[] { "HostId", "Id" }
            );

            migrationBuilder.AddUniqueConstraint(
                name: "AK_community_definitions_HostId_Id",
                table: "community_definitions",
                columns: new[] { "HostId", "Id" }
            );

            migrationBuilder.CreateTable(
                name: "moment_attachments",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    MomentCandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    BountyId = table.Column<long>(type: "INTEGER", nullable: true),
                    CommunityDefinitionId = table.Column<long>(type: "INTEGER", nullable: true),
                    CompetitionMatchId = table.Column<long>(type: "INTEGER", nullable: true),
                    AttachedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moment_attachments", x => x.Id);
                    table.CheckConstraint(
                        "CK_moment_attachments_OneDestination",
                        "(BountyId IS NOT NULL AND CommunityDefinitionId IS NULL AND CompetitionMatchId IS NULL) OR (BountyId IS NULL AND CommunityDefinitionId IS NOT NULL AND CompetitionMatchId IS NULL) OR (BountyId IS NULL AND CommunityDefinitionId IS NULL AND CompetitionMatchId IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_moment_attachments_bounties_HostId_BountyId",
                        columns: x => new { x.HostId, x.BountyId },
                        principalTable: "bounties",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_attachments_community_definitions_HostId_CommunityDefinitionId",
                        columns: x => new { x.HostId, x.CommunityDefinitionId },
                        principalTable: "community_definitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_attachments_competition_matches_HostId_CompetitionMatchId",
                        columns: x => new { x.HostId, x.CompetitionMatchId },
                        principalTable: "competition_matches",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_moment_attachments_moment_candidates_HostId_MomentCandidateId",
                        columns: x => new { x.HostId, x.MomentCandidateId },
                        principalTable: "moment_candidates",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_attachments_HostId_BountyId_MomentCandidateId",
                table: "moment_attachments",
                columns: new[] { "HostId", "BountyId", "MomentCandidateId" },
                unique: true,
                filter: "\"BountyId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_attachments_HostId_CommunityDefinitionId_MomentCandidateId",
                table: "moment_attachments",
                columns: new[] { "HostId", "CommunityDefinitionId", "MomentCandidateId" },
                unique: true,
                filter: "\"CommunityDefinitionId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_attachments_HostId_CompetitionMatchId_MomentCandidateId",
                table: "moment_attachments",
                columns: new[] { "HostId", "CompetitionMatchId", "MomentCandidateId" },
                unique: true,
                filter: "\"CompetitionMatchId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_moment_attachments_HostId_MomentCandidateId",
                table: "moment_attachments",
                columns: new[] { "HostId", "MomentCandidateId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "moment_attachments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_moment_candidates_HostId_Id",
                table: "moment_candidates"
            );

            migrationBuilder.DropUniqueConstraint(
                name: "AK_community_definitions_HostId_Id",
                table: "community_definitions"
            );
        }
    }
}
