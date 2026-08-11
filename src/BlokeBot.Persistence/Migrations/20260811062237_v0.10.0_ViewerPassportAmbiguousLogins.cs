using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_ViewerPassportAmbiguousLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "viewer_passport_ambiguous_logins",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_ambiguous_logins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passport_ambiguous_logins_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_ambiguous_logins_HostId_Login",
                table: "viewer_passport_ambiguous_logins",
                columns: new[] { "HostId", "Login" },
                unique: true
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "viewer_passport_ambiguous_logins" ("HostId", "Login", "DetectedAtUtc")
                SELECT "HostId", "Login", MAX("FirstSeenAtUtc")
                FROM "viewer_passport_logins"
                GROUP BY "HostId", "Login"
                HAVING COUNT(DISTINCT "PassportId") > 1;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "viewer_passport_ambiguous_logins");
        }
    }
}
