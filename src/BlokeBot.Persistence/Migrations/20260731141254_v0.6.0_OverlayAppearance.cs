#pragma warning disable

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v060_OverlayAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_media_assets_ContentType",
                table: "overlay_media_assets"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_media_assets_ContentType",
                table: "overlay_media_assets",
                sql: "ContentType LIKE 'image/%' OR ContentType LIKE 'audio/%' OR ContentType LIKE 'video/%'"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_ConfigurationJson",
                table: "overlay_instances"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_ConfigurationJson",
                table: "overlay_instances",
                sql: "length(ConfigurationJson) BETWEEN 1 AND 8192 AND json_valid(ConfigurationJson) AND json_type(ConfigurationJson, '$.schemaVersion') = 'integer' AND json_extract(ConfigurationJson, '$.schemaVersion') = 1"
            );

            migrationBuilder.Sql(
                """
                UPDATE overlay_instances
                SET ConfigurationJson = json_set(
                    ConfigurationJson,
                    '$.appearance',
                    json(
                        CASE Type
                            WHEN 'guessing' THEN '{"x":160,"y":690,"width":1600,"height":270,"css":""}'
                            WHEN 'giveaway' THEN '{"x":160,"y":690,"width":1600,"height":270,"css":""}'
                            WHEN 'event-feed' THEN '{"x":160,"y":690,"width":1600,"height":270,"css":""}'
                        END
                    )
                )
                WHERE Type IN ('guessing', 'giveaway', 'event-feed')
                  AND json_type(ConfigurationJson, '$.appearance') IS NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_media_assets_ContentType",
                table: "overlay_media_assets"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_media_assets_ContentType",
                table: "overlay_media_assets",
                sql: "ContentType IN ('video/mp4', 'audio/mpeg')"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_instances_ConfigurationJson",
                table: "overlay_instances"
            );

            migrationBuilder.Sql(
                """
                UPDATE overlay_instances
                SET ConfigurationJson = json_remove(ConfigurationJson, '$.appearance')
                WHERE Type IN ('guessing', 'giveaway', 'event-feed');
                """
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_instances_ConfigurationJson",
                table: "overlay_instances",
                sql: "length(ConfigurationJson) BETWEEN 1 AND 4096 AND json_valid(ConfigurationJson) AND json_type(ConfigurationJson, '$.schemaVersion') = 'integer' AND json_extract(ConfigurationJson, '$.schemaVersion') = 1"
            );
        }
    }
}
