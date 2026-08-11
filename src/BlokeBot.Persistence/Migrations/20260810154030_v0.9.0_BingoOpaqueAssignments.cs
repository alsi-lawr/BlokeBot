using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v090_BingoOpaqueAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IssuedLayout",
                table: "bingo_cards",
                type: "TEXT",
                maxLength: 16000,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE "__bingo_opaque_assignment_downgrade_guard"
                (
                    "Value" INTEGER NOT NULL
                );

                CREATE TEMP TRIGGER "__bingo_opaque_assignment_downgrade_refusal"
                BEFORE INSERT ON "__bingo_opaque_assignment_downgrade_guard"
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'Cannot downgrade Bingo cards with materialized opaque-assignment layouts.'
                    );
                END;

                INSERT INTO "__bingo_opaque_assignment_downgrade_guard" ("Value")
                SELECT 1
                WHERE EXISTS
                (
                    SELECT 1
                    FROM "bingo_cards"
                    WHERE "IssuedLayout" IS NOT NULL
                );

                DROP TRIGGER "__bingo_opaque_assignment_downgrade_refusal";
                DROP TABLE "__bingo_opaque_assignment_downgrade_guard";
                """
            );
            migrationBuilder.DropColumn(name: "IssuedLayout", table: "bingo_cards");
        }
    }
}
