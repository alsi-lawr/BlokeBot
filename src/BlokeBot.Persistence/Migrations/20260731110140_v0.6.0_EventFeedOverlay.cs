#pragma warning disable

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v060_EventFeedOverlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances"
            );

            migrationBuilder.CreateTable(
                name: "overlay_event_feed_items",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OverlayInstanceId = table.Column<long>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Lifecycle = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    EnqueuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DisplayDeadlineUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TombstoneExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_event_feed_items", x => x.Id);
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Duration",
                        "DurationSeconds BETWEEN 1 AND 30"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Kind",
                        "Kind IN ('giveawayWinner', 'guessingWinner', 'pointAward')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Lifecycle",
                        "Lifecycle IN ('active', 'consumed', 'queued', 'suppressed')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Priority",
                        "Priority IN ('high', 'normal')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_SourceKey",
                        "length(SourceKey) BETWEEN 1 AND 160"
                    );
                    table.CheckConstraint(
                        "CK_overlay_event_feed_items_Text",
                        "length(Title) BETWEEN 1 AND 160 AND length(Body) >= 1"
                    );
                    table.ForeignKey(
                        name: "FK_overlay_event_feed_items_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_overlay_event_feed_items_overlay_instances_OverlayInstanceId",
                        column: x => x.OverlayInstanceId,
                        principalTable: "overlay_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances",
                sql: "Type IN ('cue-player', 'empty', 'event-feed', 'giveaway', 'guessing')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_event_feed_items_HostId_OverlayInstanceId_Lifecycle_EnqueuedAtUtc",
                table: "overlay_event_feed_items",
                columns: new[] { "HostId", "OverlayInstanceId", "Lifecycle", "EnqueuedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_event_feed_items_OverlayInstanceId_Kind_SourceKey",
                table: "overlay_event_feed_items",
                columns: new[] { "OverlayInstanceId", "Kind", "SourceKey" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "overlay_event_feed_items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances",
                sql: "Type IN ('cue-player', 'empty', 'giveaway', 'guessing')"
            );
        }
    }
}
