using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FullOverlayWidgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "OverlayInstanceId",
                table: "overlay_event_feed_items",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER"
            );

            migrationBuilder.AddColumn<long>(
                name: "FullOverlayEventFeedBindingId",
                table: "overlay_event_feed_items",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "full_overlay_event_feed_bindings",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullOverlayId = table.Column<long>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    BindingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishedVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_full_overlay_event_feed_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_full_overlay_event_feed_bindings_full_overlays_FullOverlayId",
                        column: x => x.FullOverlayId,
                        principalTable: "full_overlays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_full_overlay_event_feed_bindings_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_event_feed_items_FullOverlayEventFeedBindingId_Kind_SourceKey",
                table: "overlay_event_feed_items",
                columns: new[] { "FullOverlayEventFeedBindingId", "Kind", "SourceKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_event_feed_items_FullOverlayEventFeedBindingId_Lifecycle_EnqueuedAtUtc",
                table: "overlay_event_feed_items",
                columns: new[] { "FullOverlayEventFeedBindingId", "Lifecycle", "EnqueuedAtUtc" }
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_event_feed_items_Owner",
                table: "overlay_event_feed_items",
                sql: "(OverlayInstanceId IS NULL) <> (FullOverlayEventFeedBindingId IS NULL)"
            );

            migrationBuilder.CreateIndex(
                name: "IX_full_overlay_event_feed_bindings_FullOverlayId_BindingId",
                table: "full_overlay_event_feed_bindings",
                columns: new[] { "FullOverlayId", "BindingId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_full_overlay_event_feed_bindings_HostId",
                table: "full_overlay_event_feed_bindings",
                column: "HostId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_overlay_event_feed_items_full_overlay_event_feed_bindings_FullOverlayEventFeedBindingId",
                table: "overlay_event_feed_items",
                column: "FullOverlayEventFeedBindingId",
                principalTable: "full_overlay_event_feed_bindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Full overlay widget migrations are forward-only.");
    }
}
