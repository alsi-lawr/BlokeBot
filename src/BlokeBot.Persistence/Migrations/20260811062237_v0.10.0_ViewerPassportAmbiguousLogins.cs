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
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "viewer_passport_ambiguous_logins" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_viewer_passport_ambiguous_logins"
                        PRIMARY KEY AUTOINCREMENT,
                    "HostId" INTEGER NOT NULL,
                    "Login" TEXT NOT NULL,
                    "DetectedAtUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_viewer_passport_ambiguous_logins_hosts_HostId"
                        FOREIGN KEY ("HostId") REFERENCES "hosts" ("Id") ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS
                    "IX_viewer_passport_ambiguous_logins_HostId_Login"
                    ON "viewer_passport_ambiguous_logins" ("HostId", "Login");

                INSERT OR IGNORE INTO "viewer_passport_ambiguous_logins"
                    ("HostId", "Login", "DetectedAtUtc")
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
            // Ambiguity tombstones are privacy safety data and must survive code rollback.
        }
    }
}
