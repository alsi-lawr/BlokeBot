using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0101_ViewerPassportStreamAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "viewer_passport_attendance_days");

            migrationBuilder.AddColumn<int>(
                name: "ViewerPassportContinuityGeneration",
                table: "hosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddUniqueConstraint(
                name: "AK_viewer_passports_HostId_Id",
                table: "viewer_passports",
                columns: new[] { "HostId", "Id" }
            );

            migrationBuilder.CreateTable(
                name: "viewer_passport_stream_sessions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    TwitchStreamId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContinuityGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_stream_sessions", x => x.Id);
                    table.UniqueConstraint(
                        "AK_viewer_passport_stream_sessions_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passport_stream_sessions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "viewer_passport_stream_attendance",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PassportId = table.Column<long>(type: "INTEGER", nullable: false),
                    StreamSessionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ContinuityGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_stream_attendance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passport_stream_attendance_viewer_passport_stream_sessions_HostId_StreamSessionId",
                        columns: x => new { x.HostId, x.StreamSessionId },
                        principalTable: "viewer_passport_stream_sessions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passport_stream_attendance_viewer_passports_HostId_PassportId",
                        columns: x => new { x.HostId, x.PassportId },
                        principalTable: "viewer_passports",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_stream_attendance_HostId_PassportId_StreamSessionId",
                table: "viewer_passport_stream_attendance",
                columns: new[] { "HostId", "PassportId", "StreamSessionId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_stream_attendance_HostId_StreamSessionId",
                table: "viewer_passport_stream_attendance",
                columns: new[] { "HostId", "StreamSessionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_stream_sessions_HostId_ContinuityGeneration_StartedAtUtc_TwitchStreamId",
                table: "viewer_passport_stream_sessions",
                columns: new[]
                {
                    "HostId",
                    "ContinuityGeneration",
                    "StartedAtUtc",
                    "TwitchStreamId",
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_stream_sessions_HostId_TwitchStreamId",
                table: "viewer_passport_stream_sessions",
                columns: new[] { "HostId", "TwitchStreamId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "viewer_passport_stream_attendance");

            migrationBuilder.DropTable(name: "viewer_passport_stream_sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_viewer_passports_HostId_Id",
                table: "viewer_passports"
            );

            migrationBuilder.DropColumn(name: "ViewerPassportContinuityGeneration", table: "hosts");

            migrationBuilder.CreateTable(
                name: "viewer_passport_attendance_days",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DateUtc = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    PassportId = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewer_passport_attendance_days", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viewer_passport_attendance_days_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_viewer_passport_attendance_days_viewer_passports_PassportId",
                        column: x => x.PassportId,
                        principalTable: "viewer_passports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_attendance_days_HostId_PassportId_DateUtc",
                table: "viewer_passport_attendance_days",
                columns: new[] { "HostId", "PassportId", "DateUtc" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_viewer_passport_attendance_days_PassportId",
                table: "viewer_passport_attendance_days",
                column: "PassportId"
            );
        }
    }
}
