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
                name: "twitch_prediction_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    PredictionWindowSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_prediction_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_prediction_templates_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "twitch_predictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderPredictionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    OutcomesJson = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsExternallyStarted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LocksAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_predictions", x => x.Id);
                    table.CheckConstraint("CK_twitch_predictions_Status", "Status IN ('Active', 'Archived', 'Canceled', 'Locked', 'Resolved')");
                    table.ForeignKey(
                        name: "FK_twitch_predictions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "twitch_prediction_template_outcomes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TwitchPredictionTemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 25, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_twitch_prediction_template_outcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_twitch_prediction_template_outcomes_twitch_prediction_templates_TwitchPredictionTemplateId",
                        column: x => x.TwitchPredictionTemplateId,
                        principalTable: "twitch_prediction_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_twitch_prediction_template_outcomes_TwitchPredictionTemplateId_Position",
                table: "twitch_prediction_template_outcomes",
                columns: new[] { "TwitchPredictionTemplateId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_twitch_prediction_templates_HostId",
                table: "twitch_prediction_templates",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_twitch_predictions_HostId",
                table: "twitch_predictions",
                column: "HostId",
                unique: true,
                filter: "\"Status\" IN ('Active', 'Locked')");

            migrationBuilder.CreateIndex(
                name: "IX_twitch_predictions_HostId_EndedAtUtc",
                table: "twitch_predictions",
                columns: new[] { "HostId", "EndedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_twitch_predictions_HostId_ProviderPredictionId",
                table: "twitch_predictions",
                columns: new[] { "HostId", "ProviderPredictionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "twitch_prediction_template_outcomes");

            migrationBuilder.DropTable(
                name: "twitch_predictions");

            migrationBuilder.DropTable(
                name: "twitch_prediction_templates");
        }
    }
}
