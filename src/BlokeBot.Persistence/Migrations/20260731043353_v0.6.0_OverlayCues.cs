using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v060_OverlayCues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances"
            );

            migrationBuilder.CreateTable(
                name: "overlay_cues",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DurationMilliseconds = table.Column<int>(type: "INTEGER", nullable: false),
                    QueuePolicy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    ConfigurationJson = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32768,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_cues", x => x.Id);
                    table.UniqueConstraint(
                        "AK_overlay_cues_Id_HostId",
                        x => new { x.Id, x.HostId }
                    );
                    table.CheckConstraint(
                        "CK_overlay_cues_ConfigurationJson",
                        "length(ConfigurationJson) BETWEEN 1 AND 32768 AND json_valid(ConfigurationJson) AND json_type(ConfigurationJson, '$.schemaVersion') = 'integer' AND json_extract(ConfigurationJson, '$.schemaVersion') = 1"
                    );
                    table.CheckConstraint(
                        "CK_overlay_cues_Duration",
                        "DurationMilliseconds BETWEEN 100 AND 300000"
                    );
                    table.CheckConstraint("CK_overlay_cues_Name", "length(Name) BETWEEN 1 AND 128");
                    table.CheckConstraint(
                        "CK_overlay_cues_QueuePolicy",
                        "QueuePolicy IN ('concurrent', 'enqueue', 'ignore', 'replace')"
                    );
                    table.CheckConstraint("CK_overlay_cues_Revision", "Revision > 0");
                    table.ForeignKey(
                        name: "FK_overlay_cues_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_media_assets",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ContentType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_media_assets", x => x.Id);
                    table.UniqueConstraint(
                        "AK_overlay_media_assets_Id_HostId",
                        x => new { x.Id, x.HostId }
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_assets_ContentType",
                        "ContentType IN ('video/mp4', 'audio/mpeg')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_assets_Length",
                        "ByteLength > 0 AND ContentRevision > 0"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_assets_Name",
                        "length(Name) BETWEEN 1 AND 128"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_assets_StorageKey",
                        "length(StorageKey) = 32"
                    );
                    table.ForeignKey(
                        name: "FK_overlay_media_assets_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_cue_media_asset_references",
                columns: table => new
                {
                    CueId = table.Column<long>(type: "INTEGER", nullable: false),
                    AssetId = table.Column<long>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_overlay_cue_media_asset_references",
                        x => new { x.CueId, x.AssetId }
                    );
                    table.ForeignKey(
                        name: "FK_overlay_cue_media_asset_references_overlay_cues_CueId_HostId",
                        columns: x => new { x.CueId, x.HostId },
                        principalTable: "overlay_cues",
                        principalColumns: new[] { "Id", "HostId" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_overlay_cue_media_asset_references_overlay_media_assets_AssetId_HostId",
                        columns: x => new { x.AssetId, x.HostId },
                        principalTable: "overlay_media_assets",
                        principalColumns: new[] { "Id", "HostId" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances",
                sql: "Type IN ('cue-player', 'empty', 'guessing')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cue_media_asset_references_AssetId_HostId",
                table: "overlay_cue_media_asset_references",
                columns: new[] { "AssetId", "HostId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cue_media_asset_references_CueId_HostId",
                table: "overlay_cue_media_asset_references",
                columns: new[] { "CueId", "HostId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cue_media_asset_references_HostId_AssetId",
                table: "overlay_cue_media_asset_references",
                columns: new[] { "HostId", "AssetId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cues_HostId_Name_PublicId",
                table: "overlay_cues",
                columns: new[] { "HostId", "Name", "PublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_cues_PublicId",
                table: "overlay_cues",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_assets_HostId_Name_PublicId",
                table: "overlay_media_assets",
                columns: new[] { "HostId", "Name", "PublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_assets_PublicId",
                table: "overlay_media_assets",
                column: "PublicId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_assets_StorageKey",
                table: "overlay_media_assets",
                column: "StorageKey",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "overlay_cue_media_asset_references");

            migrationBuilder.DropTable(name: "overlay_cues");

            migrationBuilder.DropTable(name: "overlay_media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_Type",
                table: "overlay_instances",
                sql: "Type IN ('empty', 'guessing')"
            );
        }
    }
}
