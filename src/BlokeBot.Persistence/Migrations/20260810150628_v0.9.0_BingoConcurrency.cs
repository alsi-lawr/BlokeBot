using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v090_BingoConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RosterRevision",
                table: "bingo_games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.CreateIndex(
                name: "IX_bingo_games_HostId",
                table: "bingo_games",
                column: "HostId",
                unique: true,
                filter: "\"Status\" IN ('Joining', 'Issued')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_bingo_games_HostId", table: "bingo_games");

            migrationBuilder.DropColumn(name: "RosterRevision", table: "bingo_games");
        }
    }
}
