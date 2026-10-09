using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FullOverlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "full_overlays",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DraftDocumentJson = table.Column<string>(type: "TEXT", nullable: false),
                    AccessKeyDigest = table.Column<byte[]>(
                        type: "BLOB",
                        maxLength: 32,
                        nullable: false
                    ),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    PublicationSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    PublishedVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_full_overlays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_full_overlays_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "full_overlay_publications",
                columns: table => new
                {
                    OverlayId = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    AuthorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_full_overlay_publications",
                        x => new { x.OverlayId, x.Version }
                    );
                    table.ForeignKey(
                        name: "FK_full_overlay_publications_full_overlays_OverlayId",
                        column: x => x.OverlayId,
                        principalTable: "full_overlays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_full_overlays_AccessKeyDigest",
                table: "full_overlays",
                column: "AccessKeyDigest",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_full_overlays_HostId_IsArchived_PublicId",
                table: "full_overlays",
                columns: new[] { "HostId", "IsArchived", "PublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_full_overlays_PublicId",
                table: "full_overlays",
                column: "PublicId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Full overlay migrations are forward-only.");
    }
}
