using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecentChannelChatters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recent_channel_chatters",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    TwitchUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Login = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_recent_channel_chatters",
                        x => new { x.HostId, x.TwitchUserId }
                    );
                    table.ForeignKey(
                        name: "FK_recent_channel_chatters_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_recent_channel_chatters_HostId_ObservedAtUtc_TwitchUserId",
                table: "recent_channel_chatters",
                columns: new[] { "HostId", "ObservedAtUtc", "TwitchUserId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "recent_channel_chatters");
        }
    }
}
