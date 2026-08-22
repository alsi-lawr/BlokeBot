using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0120_GuessingSharedAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_command_aliases_HostId_Alias",
                table: "command_aliases"
            );

            migrationBuilder.CreateIndex(
                name: "IX_command_aliases_HostId_Alias",
                table: "command_aliases",
                columns: new[] { "HostId", "Alias" },
                unique: true,
                filter: "\"GuessRoundProfileId\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_command_aliases_HostId_GuessRoundProfileId_Alias",
                table: "command_aliases",
                columns: new[] { "HostId", "GuessRoundProfileId", "Alias" },
                unique: true,
                filter: "\"GuessRoundProfileId\" IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_command_aliases_HostId_Alias",
                table: "command_aliases"
            );

            migrationBuilder.DropIndex(
                name: "IX_command_aliases_HostId_GuessRoundProfileId_Alias",
                table: "command_aliases"
            );

            migrationBuilder.CreateIndex(
                name: "IX_command_aliases_HostId_Alias",
                table: "command_aliases",
                columns: new[] { "HostId", "Alias" },
                unique: true
            );
        }
    }
}
