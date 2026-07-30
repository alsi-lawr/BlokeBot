using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v050_OverlayInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "overlay_instance_events",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    OverlayPublicId = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    OverlayRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    KeyVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_instance_events", x => x.Id);
                    table.CheckConstraint(
                        "CK_overlay_instance_events_Kind",
                        "Kind IN ('configured', 'created', 'deleted', 'disabled', 'enabled', 'key-rotated', 'renamed')"
                    );
                    table.ForeignKey(
                        name: "FK_overlay_instance_events_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "overlay_instances",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfigurationJson = table.Column<string>(
                        type: "TEXT",
                        maxLength: 4096,
                        nullable: false
                    ),
                    AccessKeyDigest = table.Column<byte[]>(
                        type: "BLOB",
                        maxLength: 32,
                        nullable: false
                    ),
                    KeyVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_instances", x => x.Id);
                    table.CheckConstraint(
                        "CK_overlay_instances_AccessKeyDigest",
                        "length(AccessKeyDigest) = 32"
                    );
                    table.CheckConstraint(
                        "CK_overlay_instances_ConfigurationJson",
                        "length(ConfigurationJson) BETWEEN 1 AND 4096 AND json_valid(ConfigurationJson) AND json_type(ConfigurationJson, '$.schemaVersion') = 'integer' AND json_extract(ConfigurationJson, '$.schemaVersion') = 1"
                    );
                    table.CheckConstraint(
                        "CK_overlay_instances_Name",
                        "length(Name) BETWEEN 1 AND 128"
                    );
                    table.CheckConstraint("CK_overlay_instances_Type", "Type IN ('empty')");
                    table.CheckConstraint(
                        "CK_overlay_instances_Versions",
                        "KeyVersion > 0 AND Revision > 0"
                    );
                    table.ForeignKey(
                        name: "FK_overlay_instances_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_instance_events_HostId_OverlayPublicId_Id",
                table: "overlay_instance_events",
                columns: new[] { "HostId", "OverlayPublicId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_instances_AccessKeyDigest",
                table: "overlay_instances",
                column: "AccessKeyDigest",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_instances_HostId_UpdatedAtUtc_PublicId",
                table: "overlay_instances",
                columns: new[] { "HostId", "UpdatedAtUtc", "PublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_instances_PublicId",
                table: "overlay_instances",
                column: "PublicId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "overlay_instance_events");

            migrationBuilder.DropTable(name: "overlay_instances");
        }
    }
}
