using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_ShoutoutRaidMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NativeShoutoutEnabled",
                table: "raid_collaboration_settings"
            );

            migrationBuilder.AddColumn<bool>(
                name: "OnlyApprovedChannels",
                table: "automatic_raid_shoutout_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnlyApprovedChannels",
                table: "automatic_raid_shoutout_settings"
            );

            migrationBuilder.AddColumn<bool>(
                name: "NativeShoutoutEnabled",
                table: "raid_collaboration_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true
            );
        }
    }
}
