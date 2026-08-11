using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_ViewerPassportLoginHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "viewer_passport_logins",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PassportId = table.Column<long>(type: "INTEGER", nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_logins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passport_logins_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passport_logins_viewer_passports_PassportId",
                        column: x => x.PassportId,
                        principalTable: "viewer_passports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_logins_HostId_Login",
                table: "viewer_passport_logins",
                columns: new[] { "HostId", "Login" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_logins_HostId_PassportId_Login",
                table: "viewer_passport_logins",
                columns: new[] { "HostId", "PassportId", "Login" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_logins_PassportId",
                table: "viewer_passport_logins",
                column: "PassportId"
            );

            migrationBuilder.Sql(
                """
                INSERT INTO viewer_passport_logins
                    (HostId, PassportId, Login, FirstSeenAtUtc, LastSeenAtUtc)
                SELECT HostId, Id, Login, CreatedAtUtc, UpdatedAtUtc
                FROM viewer_passports
                WHERE Login <> '';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "viewer_passport_logins");
        }
    }
}
