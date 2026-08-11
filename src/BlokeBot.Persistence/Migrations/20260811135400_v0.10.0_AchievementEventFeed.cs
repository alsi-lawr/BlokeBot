using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_AchievementEventFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_event_feed_items_Kind",
                table: "overlay_event_feed_items"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_event_feed_items_Kind",
                table: "overlay_event_feed_items",
                sql: "Kind IN ('achievementCompletion', 'bingoEvent', 'giveawayWinner', 'guessingWinner', 'pointAward')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_overlay_event_feed_items_Kind",
                table: "overlay_event_feed_items"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_overlay_event_feed_items_Kind",
                table: "overlay_event_feed_items",
                sql: "Kind IN ('bingoEvent', 'giveawayWinner', 'guessingWinner', 'pointAward')"
            );
        }
    }
}
