using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0120 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH migration_reference(value) AS (
                    SELECT strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                )
                UPDATE custom_announcement_schedules
                SET WeeklyDay = blokebot_weekly_utc_day(
                        WeeklyDay,
                        WeeklyTime,
                        (SELECT TimeZoneId FROM hosts WHERE hosts.Id = custom_announcement_schedules.HostId),
                        (SELECT value FROM migration_reference)
                    ),
                    WeeklyTime = blokebot_weekly_utc_time(
                        WeeklyDay,
                        WeeklyTime,
                        (SELECT TimeZoneId FROM hosts WHERE hosts.Id = custom_announcement_schedules.HostId),
                        (SELECT value FROM migration_reference)
                    )
                WHERE ScheduleType = 'Weekly';
                """
            );

            migrationBuilder.CreateTable(
                name: "configuration_activations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnabledChanges = table.Column<long>(type: "INTEGER", nullable: false),
                    DisabledChanges = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_activations", x => x.Id);
                    table.CheckConstraint(
                        "CK_configuration_activations_Status",
                        "Status IN ('Complete', 'Failed', 'Pending', 'Processing')"
                    );
                    table.ForeignKey(
                        name: "FK_configuration_activations_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "configuration_import_audits",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false),
                    ActorTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    SourceFormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SummaryJson = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_import_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_import_audits_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_configuration_activations_HostId_Status",
                table: "configuration_activations",
                columns: new[] { "HostId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_configuration_import_audits_HostId_OccurredAtUtc",
                table: "configuration_import_audits",
                columns: new[] { "HostId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_configuration_import_audits_OperationId",
                table: "configuration_import_audits",
                column: "OperationId",
                unique: true
            );

            migrationBuilder.CreateTable(
                name: "overlay_media_documents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: false
                    ),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LegacyHostId = table.Column<int>(type: "INTEGER", nullable: true),
                    LegacyStorageKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 32,
                        nullable: true
                    ),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OrphanedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overlay_media_documents", x => x.Id);
                    table.CheckConstraint(
                        "CK_overlay_media_documents_ContentType",
                        "ContentType LIKE 'image/%' OR ContentType LIKE 'audio/%' OR ContentType LIKE 'video/%'"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_documents_Legacy",
                        "(LegacyHostId IS NULL AND LegacyStorageKey IS NULL) OR (LegacyHostId IS NOT NULL AND length(LegacyStorageKey) = 32)"
                    );
                    table.CheckConstraint("CK_overlay_media_documents_Length", "ByteLength > 0");
                    table.CheckConstraint(
                        "CK_overlay_media_documents_State",
                        "State IN ('available', 'orphaned', 'publishing', 'unavailable')"
                    );
                    table.CheckConstraint(
                        "CK_overlay_media_documents_StorageKey",
                        "length(StorageKey) = 32"
                    );
                }
            );

            migrationBuilder.AddColumn<string>(
                name: "DocumentId",
                table: "overlay_media_assets",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.Sql(
                """
                INSERT INTO overlay_media_documents (
                    Id, ContentType, ByteLength, StorageKey, State,
                    LegacyHostId, LegacyStorageKey, CreatedAtUtc, UpdatedAtUtc, OrphanedAtUtc
                )
                SELECT
                    PublicId, ContentType, ByteLength, StorageKey, 'publishing',
                    HostId, StorageKey, CreatedAtUtc, UpdatedAtUtc, NULL
                FROM overlay_media_assets;

                UPDATE overlay_media_assets SET DocumentId = PublicId;
                """
            );

            migrationBuilder.DropIndex(
                name: "IX_overlay_media_assets_StorageKey",
                table: "overlay_media_assets"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_media_assets_ContentType",
                table: "overlay_media_assets"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_media_assets_Length",
                table: "overlay_media_assets"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_media_assets_StorageKey",
                table: "overlay_media_assets"
            );

            migrationBuilder.DropColumn(name: "ByteLength", table: "overlay_media_assets");

            migrationBuilder.DropColumn(name: "ContentType", table: "overlay_media_assets");

            migrationBuilder.DropColumn(name: "StorageKey", table: "overlay_media_assets");

            migrationBuilder.AlterColumn<string>(
                name: "DocumentId",
                table: "overlay_media_assets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_assets_DocumentId",
                table: "overlay_media_assets",
                column: "DocumentId"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_media_assets_Length",
                table: "overlay_media_assets",
                sql: "ContentRevision > 0"
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_documents_State_UpdatedAtUtc",
                table: "overlay_media_documents",
                columns: new[] { "State", "UpdatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_documents_StorageKey",
                table: "overlay_media_documents",
                column: "StorageKey",
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_overlay_media_assets_overlay_media_documents_DocumentId",
                table: "overlay_media_assets",
                column: "DocumentId",
                principalTable: "overlay_media_documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_overlay_media_assets_overlay_media_documents_DocumentId",
                table: "overlay_media_assets"
            );

            migrationBuilder.DropIndex(
                name: "IX_overlay_media_assets_DocumentId",
                table: "overlay_media_assets"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_media_assets_Length",
                table: "overlay_media_assets"
            );

            migrationBuilder.AddColumn<long>(
                name: "ByteLength",
                table: "overlay_media_assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "overlay_media_assets",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "overlay_media_assets",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.Sql(
                """
                UPDATE overlay_media_assets
                SET ContentType = (
                        SELECT ContentType FROM overlay_media_documents
                        WHERE Id = overlay_media_assets.DocumentId
                    ),
                    ByteLength = (
                        SELECT ByteLength FROM overlay_media_documents
                        WHERE Id = overlay_media_assets.DocumentId
                    ),
                    StorageKey = (
                        SELECT StorageKey FROM overlay_media_documents
                        WHERE Id = overlay_media_assets.DocumentId
                    );
                """
            );

            migrationBuilder.DropTable(name: "overlay_media_documents");

            migrationBuilder.DropColumn(name: "DocumentId", table: "overlay_media_assets");

            migrationBuilder.CreateIndex(
                name: "IX_overlay_media_assets_StorageKey",
                table: "overlay_media_assets",
                column: "StorageKey",
                unique: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_media_assets_ContentType",
                table: "overlay_media_assets",
                sql: "ContentType LIKE 'image/%' OR ContentType LIKE 'audio/%' OR ContentType LIKE 'video/%'"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_media_assets_Length",
                table: "overlay_media_assets",
                sql: "ByteLength > 0 AND ContentRevision > 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_media_assets_StorageKey",
                table: "overlay_media_assets",
                sql: "length(StorageKey) = 32"
            );

            migrationBuilder.DropTable(name: "configuration_activations");

            migrationBuilder.DropTable(name: "configuration_import_audits");
        }
    }
}
